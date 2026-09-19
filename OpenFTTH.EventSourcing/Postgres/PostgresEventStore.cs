using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing.Postgres
{
    public sealed class PostgresEventStore : IEventStore
    {
        private readonly string _databaseSchema = "events";
        private long _lastSequenceNumberProcessed;
        private ConcurrentDictionary<Guid, bool> _inlineEventsNotCatchedUpYet = new();
        private StringEnumConverter _stringEnumConverter = new StringEnumConverter();

        public long NumberOfInlineEventsNotCatchedUp => _inlineEventsNotCatchedUpYet.Count;

        private ProjectionRepository _projectionRepository;
        public IProjectionRepository Projections => _projectionRepository;

        private AggregateRepository _aggregateRepository;
        public IAggregateRepository Aggregates => _aggregateRepository;

        private ISequences _sequences;
        public ISequences Sequences => _sequences;

        private readonly string _connectionString;

        public PostgresEventStore(IServiceProvider serviceProvider, string connectionString, string databaseSchemaName, bool cleanAll = false)
        {
            _connectionString = connectionString;
            _aggregateRepository = new AggregateRepository(this);
            _projectionRepository = new ProjectionRepository(serviceProvider);
            _sequences = new PostgresSequenceStore(connectionString, databaseSchemaName);

            // TODO create schema
        }

        private static readonly MethodInfo ApplyEvent = typeof(AggregateBase).GetMethod("ApplyEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        public T Load<T>(Guid id, long? version = null) where T : AggregateBase
        {
            var queryStreamSql = $@"
select *
from {_databaseSchema}.mt_events
where version > @version and stream_id = '@streamId'
order by version asc";

            using var conn = new NpgsqlConnection(_connectionString);
            using var cmd = new NpgsqlCommand(queryStreamSql, conn);
            cmd.Parameters.AddWithValue("@streamId", id);
            cmd.Parameters.AddWithValue("@version", version ?? 0);

            var types = new Dictionary<string, Type>();
            var events = new List<object>();

            conn.Open();
            var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var (assemblyName, typeName) = GetMartenDotNetTypeFormat((string)reader["mt_dotnet_type"]);

                if (!types.ContainsKey(typeName))
                {
                    types.Add(typeName, LoadType(assemblyName, typeName));
                }

                events.Add(JsonConvert.DeserializeObject((string)reader["data"], types[typeName]));
            }

            if (events != null && events.Any())
            {
                var instance = Activator.CreateInstance(typeof(T), true);

                // Replay our aggregate state from the event stream
                events.Aggregate(instance, (o, e) => ApplyEvent.Invoke(instance, new[] { e }));

                return (T)instance;
            }

            throw new InvalidOperationException($"No aggregate by id {id}.");
        }

        public bool CheckIfAggregateIdHasBeenUsed(Guid id)
        {
            return CurrentStreamVersion(id) is not null;
        }

        public void AppendStream(Guid streamId, long expectedVersion, object[] events)
        {
            const string insertSql = @"
INSERT INTO events.mt_events
(seq_id, id, stream_id, version, data, type, mt_dotnet_type, timestamp)
VALUES(@seqId, @id, @streamId, @version, @data, @type, @dotnetType, @timeStamp);";

            var maxRetries = 20;
            var retry = 0;

            var initialStreamVersion = CurrentStreamVersion(streamId);
            var versionNumber = initialStreamVersion;
            if (versionNumber + events.Count() != expectedVersion)
            {
                throw new ApplicationException($"Expected stream version {streamId} does not match the expected version number.");
            }

            while (true)
            {
                using var conn = new NpgsqlConnection(_connectionString);
                conn.Open();

                var transaction = conn.BeginTransaction();

                var newestSequenceNumber = GetNewestSequenceNumber() ?? 0L;
                var newSequenceNumber = newestSequenceNumber;
                var eventIds = new List<Guid>(events.Count());

                foreach (var uncommitedEvent in events)
                {
                    var eventId = Guid.NewGuid();
                    eventIds.Add(eventId);
                    newSequenceNumber++;
                    versionNumber++;

                    var eventTypeFullName = uncommitedEvent.GetType().FullName;
                    var eventTypeNameSnakeCase = ToSnakeCase(eventTypeFullName.Split(".").Last());

                    using var command = new NpgsqlCommand(insertSql, conn, transaction)
                    {
                        Parameters =
                            {
                                new ("@seqId", newSequenceNumber),
                                new ("@id", eventId),
                                new ("@streamId", streamId),
                                new ("@version", newSequenceNumber),
                                new NpgsqlParameter("@data", NpgsqlDbType.Jsonb)
                                {
                                    Value = JsonConvert.SerializeObject(uncommitedEvent, _stringEnumConverter)
                                },
                                new ("@type", eventTypeNameSnakeCase),
                                new ("@dotnetType", uncommitedEvent.GetType().FullName),
                                new ("@timeStamp", DateTime.Now),
                            }
                    };

                    command.ExecuteNonQuery();
                }

                if (newestSequenceNumber == (GetNewestSequenceNumber() ?? 0L))
                {
                    var newVersionNumber = CurrentStreamVersion(streamId);
                    if (newVersionNumber != initialStreamVersion)
                    {
                        throw new ApplicationException($"Expected stream version {streamId} does not match the expected version number.");
                    }

                    transaction.Commit();

                    foreach (var eId in eventIds)
                    {
                        _inlineEventsNotCatchedUpYet.TryAdd(eId, true);
                    }

                    break;
                }
                else
                {
                    transaction.Rollback();

                    if (retry == maxRetries)
                    {
                        throw new ApplicationException("Reached max retries for insertions");
                    }

                    retry++;
                }
            }
        }

        public async Task AppendStreamAsync(Guid streamId, long expectedVersion, object[] events)
        {
            const string insertSql = @"
INSERT INTO events.mt_events
(seq_id, id, stream_id, version, data, type, mt_dotnet_type, timestamp)
VALUES(@seqId, @id, @streamId, @version, @data, @type, @dotnetType, @timeStamp);";

            var maxRetries = 20;
            var retry = 0;

            var initialStreamVersion = CurrentStreamVersion(streamId);
            var versionNumber = initialStreamVersion;

            if (versionNumber + events.Count() != expectedVersion)
            {
                throw new ApplicationException($"Expected stream version {streamId} does not match the expected version number.");
            }

            while (true)
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync().ConfigureAwait(false);

                var transaction = await conn.BeginTransactionAsync().ConfigureAwait(false);

                var newestSequenceNumber = GetNewestSequenceNumber() ?? 0L;
                var newSequenceNumber = newestSequenceNumber;
                var eventIds = new List<Guid>(events.Count());

                foreach (var uncommitedEvent in events)
                {
                    var eventId = Guid.NewGuid();
                    eventIds.Add(eventId);
                    newSequenceNumber++;
                    versionNumber++;

                    var eventTypeFullName = uncommitedEvent.GetType().FullName;
                    var eventTypeNameSnakeCase = ToSnakeCase(eventTypeFullName.Split(".").Last());

                    using var command = new NpgsqlCommand(insertSql, conn, transaction)
                    {
                        Parameters =
                            {
                                new ("@seqId", newSequenceNumber),
                                new ("@id", eventId),
                                new ("@streamId", streamId),
                                new ("@version", newSequenceNumber),
                                new NpgsqlParameter("@data", NpgsqlDbType.Jsonb)
                                {
                                    Value = JsonConvert.SerializeObject(uncommitedEvent, _stringEnumConverter)
                                },
                                new ("@type", eventTypeNameSnakeCase),
                                new ("@dotnetType", uncommitedEvent.GetType().FullName),
                                new ("@timeStamp", DateTime.Now),
                            }
                    };

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                    _inlineEventsNotCatchedUpYet.TryAdd(eventId, true);
                }

                if (newestSequenceNumber == (GetNewestSequenceNumber() ?? 0L))
                {
                    var newVersionNumber = await CurrentStreamVersionAsync(streamId).ConfigureAwait(false);
                    if (newVersionNumber == initialStreamVersion)
                    {
                        throw new ApplicationException($"Expected stream version {streamId} does not match the expected version number.");
                    }

                    await transaction.CommitAsync().ConfigureAwait(false);
                    break;
                }
                else
                {
                    await transaction.RollbackAsync().ConfigureAwait(false);

                    if (retry == maxRetries)
                    {
                        throw new ApplicationException("Reached max retries for insertions");
                    }

                    retry++;
                }
            }
        }

        public void AppendStream(IReadOnlyList<AggregateBase> aggregates)
        {
            const string insertSql = @"
INSERT INTO events.mt_events
(seq_id, id, stream_id, version, data, type, mt_dotnet_type, timestamp)
VALUES(@seqId, @id, @streamId, @version, @data, @type, @dotnetType, @timeStamp);";

            var maxRetries = 20;
            var retry = 0;

            while (true)
            {
                using var conn = new NpgsqlConnection(_connectionString);
                conn.Open();

                var transaction = conn.BeginTransaction();

                var newestSequenceNumber = GetNewestSequenceNumber() ?? 0L;
                var newSequenceNumber = newestSequenceNumber;
                var eventIds = new List<Guid>();

                foreach (var aggregate in aggregates)
                {
                    var versionNumber = aggregate.Version;

                    foreach (var uncommitedEvent in aggregate.GetUncommittedEvents())
                    {
                        var eventId = Guid.NewGuid();
                        eventIds.Add(eventId);
                        newSequenceNumber++;
                        versionNumber++;

                        var eventTypeFullName = uncommitedEvent.GetType().FullName;
                        var eventTypeNameSnakeCase = ToSnakeCase(eventTypeFullName.Split(".").Last());

                        using var command = new NpgsqlCommand(insertSql, conn, transaction)
                        {
                            Parameters =
                            {
                                new ("@seqId", newSequenceNumber),
                                new ("@id", eventId),
                                new ("@streamId", aggregate.Id),
                                new ("@version", newSequenceNumber),
                                new NpgsqlParameter("@data", NpgsqlDbType.Jsonb)
                                {
                                    Value = JsonConvert.SerializeObject(uncommitedEvent, _stringEnumConverter)
                                },
                                new ("@type", eventTypeNameSnakeCase),
                                new ("@dotnetType", uncommitedEvent.GetType().FullName),
                                new ("@timeStamp", DateTime.Now),
                            }
                        };

                        command.ExecuteNonQuery();
                    }
                }

                if (newestSequenceNumber == (GetNewestSequenceNumber() ?? 0L))
                {
                    transaction.Commit();
                    foreach (var eId in eventIds)
                    {
                        _inlineEventsNotCatchedUpYet.TryAdd(eId, true);
                    }

                    break;
                }
                else
                {
                    transaction.Rollback();

                    if (retry == maxRetries)
                    {
                        throw new ApplicationException("Reached max retries for insertions");
                    }

                    retry++;
                }
            }
        }

        public async Task AppendStreamAsync(IReadOnlyList<AggregateBase> aggregates)
        {
            const string insertSql = @"
INSERT INTO events.mt_events
(seq_id, id, stream_id, version, data, type, mt_dotnet_type, timestamp)
VALUES(@seqId, @id, @streamId, @version, @data, @type, @dotnetType, @timeStamp);";

            while (true)
            {
                using var conn = new NpgsqlConnection(_connectionString);
                conn.Open();

                var transaction = await conn.BeginTransactionAsync().ConfigureAwait(false);

                var newestSequenceNumber = GetNewestSequenceNumber() ?? 0L;
                var newSequenceNumber = newestSequenceNumber;
                var eventIds = new List<Guid>();

                foreach (var aggregate in aggregates)
                {
                    var versionNumber = aggregate.Version;

                    foreach (var uncommitedEvent in aggregate.GetUncommittedEvents())
                    {
                        var eventId = Guid.NewGuid();
                        eventIds.Add(eventId);
                        newSequenceNumber++;
                        versionNumber++;

                        var eventTypeFullName = uncommitedEvent.GetType().FullName;
                        var eventTypeNameSnakeCase = ToSnakeCase(eventTypeFullName.Split(".").Last());

                        using var command = new NpgsqlCommand(insertSql, conn, transaction)
                        {
                            Parameters =
                            {
                                new ("@seqId", newSequenceNumber),
                                new ("@id", eventId),
                                new ("@streamId", aggregate.Id),
                                new ("@version", newSequenceNumber),
                                new NpgsqlParameter("@data", NpgsqlDbType.Jsonb)
                                {
                                    Value = JsonConvert.SerializeObject(uncommitedEvent, _stringEnumConverter)
                                },
                                new ("@type", eventTypeNameSnakeCase),
                                new ("@dotnetType", uncommitedEvent.GetType().FullName),
                                new ("@timeStamp", DateTime.Now),
                            }
                        };

                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }

                if (newestSequenceNumber == (GetNewestSequenceNumber() ?? 0L))
                {
                    await transaction.CommitAsync().ConfigureAwait(false);

                    foreach (var eId in eventIds)
                    {
                        _inlineEventsNotCatchedUpYet.TryAdd(eId, true);
                    }

                    break;
                }
                else
                {
                    await transaction.RollbackAsync().ConfigureAwait(false);
                }
            }
        }

        public object[] FetchStream(Guid streamId, long version = 0)
        {
            var QUERY_EVENTS = $@"
SELECT seq_id, id, version, stream_id, timestamp, data, mt_dotnet_type
FROM events.mt_events
WHERE stream_id = @streamId AND version >= @version)
ORDER BY seq_id asc";

            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(QUERY_EVENTS, conn);
            using var reader = cmd.ExecuteReader();

            cmd.Parameters.AddWithValue("@streamId", streamId);
            cmd.Parameters.AddWithValue("@version", version);

            var types = new Dictionary<string, Type>();
            var events = new List<object>();

            while (reader.Read())
            {
                var (assemblyName, typeName) = GetMartenDotNetTypeFormat((string)reader["mt_dotnet_type"]);

                if (!types.ContainsKey(typeName))
                {
                    types.Add(typeName, LoadType(assemblyName, typeName));
                }

                events.Add(JsonConvert.DeserializeObject((string)reader["data"], types[typeName]));
            }

            return events.ToArray();
        }

        public void DehydrateProjections()
        {
            var eventTypesInClause = String.Join(
                ", ",
                GetMartenDotNetTypeFormat(_projectionRepository.GetAll()).Select(x => $"'{x}'")
            );

            var QUERY_EVENTS = $@"
SELECT data, mt_dotnet_type
FROM events.mt_events
WHERE mt_dotnet_type IN ({eventTypesInClause})
ORDER BY seq_id asc";

            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(QUERY_EVENTS, conn);
            using var reader = cmd.ExecuteReader();

            var types = new Dictionary<string, Type>();

            while (reader.Read())
            {
                var (assemblyName, typeName) = GetMartenDotNetTypeFormat((string)reader["mt_dotnet_type"]);

                if (!types.ContainsKey(typeName))
                {
                    types.Add(typeName, LoadType(assemblyName, typeName));
                }

                var sequenceId = Convert.ToInt64(reader["seq_id"]);

                var eventEnvelope = new EventEnvelope(
                    Guid.Parse(Convert.ToString(reader["stream_id"])),
                    Guid.Parse(Convert.ToString(reader["id"])),
                    Convert.ToInt32(reader["version"]),
                    sequenceId,
                    DateTime.Parse(Convert.ToString(reader["timestamp"])).ToUniversalTime(),
                    JsonConvert.DeserializeObject((string)reader["data"], types[typeName])
                );

                _lastSequenceNumberProcessed = sequenceId;

                _projectionRepository.ApplyEvent(eventEnvelope);
            }

            _projectionRepository.DehydrationFinish();
        }

        public async Task DehydrateProjectionsAsync(CancellationToken cancellationToken = default)
        {
            var eventTypesInClause = String.Join(
                ", ",
                GetMartenDotNetTypeFormat(_projectionRepository.GetAll()).Select(x => $"'{x}'")
            );

            var QUERY_EVENTS = $@"
SELECT seq_id, id, version, stream_id, timestamp, data, mt_dotnet_type
FROM events.mt_events
WHERE mt_dotnet_type IN ({eventTypesInClause})
ORDER BY seq_id asc";

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            using var cmd = new NpgsqlCommand(QUERY_EVENTS, conn);
            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            var types = new Dictionary<string, Type>();

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var (assemblyName, typeName) = GetMartenDotNetTypeFormat((string)reader["mt_dotnet_type"]);

                if (!types.ContainsKey(typeName))
                {
                    types.Add(typeName, LoadType(assemblyName, typeName));
                }

                var sequenceId = Convert.ToInt64(reader["seq_id"]);

                var eventEnvelope = new EventEnvelope(
                    Guid.Parse(Convert.ToString(reader["stream_id"])),
                    Guid.Parse(Convert.ToString(reader["id"])),
                    Convert.ToInt32(reader["version"]),
                    sequenceId,
                    DateTime.Parse(Convert.ToString(reader["timestamp"])).ToUniversalTime(),
                    JsonConvert.DeserializeObject((string)reader["data"], types[typeName])
                );

                _lastSequenceNumberProcessed = sequenceId;

                await _projectionRepository.ApplyEventAsync(eventEnvelope).ConfigureAwait(false);
            }

            await _projectionRepository.DehydrationFinishAsync().ConfigureAwait(false);
        }

        public long CatchUp()
        {
            var newestSequenceNumber = GetNewestSequenceNumber() ?? 0L;
            if (newestSequenceNumber == _lastSequenceNumberProcessed)
            {
                return 0;
            }

            var eventTypesInClause = String.Join(
                ", ",
                GetMartenDotNetTypeFormat(_projectionRepository.GetAll()).Select(x => $"'{x}'")
            );

            var CATCH_UP_EVENTS_SQL = $@"
SELECT seq_id, id, version, stream_id, timestamp, data, mt_dotnet_type
FROM events.mt_events
where mt_dotnet_type IN ({eventTypesInClause})
and seq_id > {_lastSequenceNumberProcessed} and seq_id <= {newestSequenceNumber}
ORDER BY seq_id asc";

            long eventsProcessed = 0;

            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(CATCH_UP_EVENTS_SQL, conn);
            using var reader = cmd.ExecuteReader();

            var types = new Dictionary<string, Type>();

            while (reader.Read())
            {
                eventsProcessed++;

                var (assemblyName, typeName) = GetMartenDotNetTypeFormat((string)reader["mt_dotnet_type"]);

                if (!types.ContainsKey(typeName))
                {
                    types.Add(typeName, LoadType(assemblyName, typeName));
                }

                var sequenceId = Convert.ToInt64(reader["seq_id"]);
                var eventId = Guid.Parse(Convert.ToString(reader["id"]));

                if (_inlineEventsNotCatchedUpYet.ContainsKey(eventId))
                {
                    // Do nothing but remove the event id from the inline event dictionary to free up memory
                    _inlineEventsNotCatchedUpYet.TryRemove(eventId, out var _);
                }
                else
                {
                    var eventEnvelope = new EventEnvelope(
                        Guid.Parse(Convert.ToString(reader["stream_id"])),
                        eventId,
                        Convert.ToInt32(reader["version"]),
                        sequenceId,
                        DateTime.Parse(Convert.ToString(reader["timestamp"])).ToUniversalTime(),
                        JsonConvert.DeserializeObject((string)reader["data"], types[typeName])
                    );

                    // Because the event id don't exist in the inline event dictionary, it must be an external event that has to be applied to projectionsd
                    _projectionRepository.ApplyEvent(eventEnvelope);
                }
            }

            _lastSequenceNumberProcessed = newestSequenceNumber;

            return eventsProcessed;
        }

        public async Task<long> CatchUpAsync(CancellationToken cancellationToken = default)
        {
            var newestSequenceNumber = GetNewestSequenceNumber() ?? 0L;
            if (newestSequenceNumber == _lastSequenceNumberProcessed)
            {
                return 0;
            }

            var eventTypesInClause = String.Join(
                ", ",
                GetMartenDotNetTypeFormat(_projectionRepository.GetAll()).Select(x => $"'{x}'")
            );

            var CATCH_UP_EVENTS_SQL = $@"
SELECT seq_id, id, version, stream_id, timestamp, data, mt_dotnet_type
FROM events.mt_events
where mt_dotnet_type IN ({eventTypesInClause})
and seq_id > {_lastSequenceNumberProcessed} and seq_id <= {newestSequenceNumber}
ORDER BY seq_id asc";

            long eventsProcessed = 0;

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            using var cmd = new NpgsqlCommand(CATCH_UP_EVENTS_SQL, conn);
            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            var types = new Dictionary<string, Type>();

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                eventsProcessed++;

                var (assemblyName, typeName) = GetMartenDotNetTypeFormat((string)reader["mt_dotnet_type"]);

                if (!types.ContainsKey(typeName))
                {
                    types.Add(typeName, LoadType(assemblyName, typeName));
                }

                var sequenceId = Convert.ToInt64(reader["seq_id"]);
                var eventId = Guid.Parse(Convert.ToString(reader["id"]));

                if (_inlineEventsNotCatchedUpYet.ContainsKey(eventId))
                {
                    // Do nothing but remove the event id from the inline event dictionary to free up memory
                    _inlineEventsNotCatchedUpYet.TryRemove(eventId, out var _);
                }
                else
                {
                    var eventEnvelope = new EventEnvelope(
                        Guid.Parse(Convert.ToString(reader["stream_id"])),
                        eventId,
                        Convert.ToInt32(reader["version"]),
                        sequenceId,
                        DateTime.Parse(Convert.ToString(reader["timestamp"])).ToUniversalTime(),
                        JsonConvert.DeserializeObject((string)reader["data"], types[typeName])
                    );

                    // Because the event id don't exist in the inline event dictionary, it must be an external event that has to be applied to projectionsd
                    await _projectionRepository.ApplyEventAsync(eventEnvelope).ConfigureAwait(false);
                }
            }

            _lastSequenceNumberProcessed = newestSequenceNumber;

            return eventsProcessed;
        }

        private static List<string> GetMartenDotNetTypeFormat(List<IProjection> projections)
            =>
            projections
            .SelectMany(x => x.GetHandlerEventTypes())
            .Select(x => $"{x.FullName}, {x.Assembly.GetName().Name}")
            .Distinct() // We Distinct to remove all duplicates
            .ToList();

        private long? GetNewestSequenceNumber()
        {
            string sql = $"SELECT MAX(seq_id) FROM {_databaseSchema}.mt_events";
            using var conn = new NpgsqlConnection(_connectionString);
            using var cmd = new NpgsqlCommand(sql, conn);

            conn.Open();
            var result = cmd.ExecuteScalar();

            return (result is not null && result is not DBNull) ? (long)result : null;
        }

        public long? CurrentStreamVersion(Guid streamId)
        {
            const string sql = "SELECT version FROM events.mt_streams where id = @id";
            using var conn = new NpgsqlConnection(_connectionString);
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", streamId);

            conn.Open();
            var result = cmd.ExecuteScalar();

            return (long?)result;
        }

        public async Task<long?> CurrentStreamVersionAsync(Guid streamId)
        {
            const string sql = "SELECT version FROM events.mt_streams where id = @id";
            using var conn = new NpgsqlConnection(_connectionString);
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", streamId);

            await conn.OpenAsync().ConfigureAwait(false);
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);

            return (long?)result;
        }

        public void ScanForProjections()
        {
            _projectionRepository.ScanServiceProviderForProjections();
        }

        private static Type LoadType(string assemblyName, string typeName)
        {
            return Assembly.Load(assemblyName).GetType(typeName);
        }

        private static (string assemblyName, string typeName) GetMartenDotNetTypeFormat(string martenDotnetType)
        {
            var splittedDotnetType = martenDotnetType.Split(",");
            return (splittedDotnetType[1], splittedDotnetType[0]);
        }

        private static string ToSnakeCase(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (text.Length < 2)
            {
                return text.ToLowerInvariant();
            }

            var sb = new StringBuilder();
            sb.Append(char.ToLowerInvariant(text[0]));

            for (int i = 1; i < text.Length; ++i)
            {
                char c = text[i];
                if (char.IsUpper(c))
                {
                    sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
