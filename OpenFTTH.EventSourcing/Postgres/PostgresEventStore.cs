using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Newtonsoft.Json;
using Npgsql;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing.Postgres
{
    public sealed class PostgresEventStore : IEventStore
    {
        private readonly IDocumentStore _store;

        private long _lastSequenceNumberProcessed;

        private ConcurrentDictionary<Guid, bool> _inlineEventsNotCatchedUpYet = new();

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
            _aggregateRepository = new AggregateRepository(this);
            _projectionRepository = new ProjectionRepository(serviceProvider);

            var options = new StoreOptions();
            options.Connection(connectionString);
            options.Projections.Add(new Projection(_projectionRepository), ProjectionLifecycle.Inline);

            // Serialize enums as strings
            var serializer = new Marten.Services.JsonNetSerializer();
            serializer.EnumStorage = Weasel.Core.EnumStorage.AsString;
            options.Serializer(serializer);

            // Can be overridden
            options.AutoCreateSchemaObjects = Weasel.Postgresql.AutoCreate.CreateOnly;
            options.DatabaseSchemaName = databaseSchemaName;

            _store = new DocumentStore(options);

            if (cleanAll)
                _store.Advanced.Clean.CompletelyRemoveAll();

            _sequences = new PostgresSequenceStore(connectionString, databaseSchemaName);

            _connectionString = connectionString;

            // This is done to force creation of the schema in case it does not exist.
            // This is needed since we no longer query using the light weight session.
            using var session = _store.LightweightSession();
            var _ = session.RequestCount;
        }

        private static readonly MethodInfo ApplyEvent = typeof(AggregateBase).GetMethod("ApplyEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        public T Load<T>(Guid id, long? version = null) where T : AggregateBase
        {
            var queryStreamSql = $@"
select *
from {_store.Options.DatabaseSchemaName}.mt_events
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
            using var session = _store.LightweightSession();
            var action = session.Events.Append(streamId, expectedVersion, events);

            // Add event ids to inline event dictionary used to prevent events to be applied to projections again when catchup is called to retrieve events produced by other services
            foreach (var e in action.Events)
            {
                _inlineEventsNotCatchedUpYet.TryAdd(e.Id, true);
            }

            session.SaveChanges();
        }

        public async Task AppendStreamAsync(Guid streamId, long expectedVersion, object[] events)
        {
            await using var session = _store.LightweightSession();
            var action = session.Events.Append(streamId, expectedVersion, events);

            // Add event ids to inline event dictionary used to prevent events to be applied to projections again when catchup is called to retrieve events produced by other services
            foreach (var e in action.Events)
            {
                _inlineEventsNotCatchedUpYet.TryAdd(e.Id, true);
            }

            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        public void AppendStream(IReadOnlyList<AggregateBase> aggregates)
        {
            using var session = _store.LightweightSession();

            foreach (var aggregate in aggregates)
            {
                var action = session.Events.Append(
                    aggregate.Id,
                    aggregate.Version,
                    aggregate.GetUncommittedEvents());

                foreach (var e in action.Events)
                {
                    _inlineEventsNotCatchedUpYet.TryAdd(e.Id, true);
                }
            }

            session.SaveChanges();
        }

        public async Task AppendStreamAsync(IReadOnlyList<AggregateBase> aggregates)
        {
            await using var session = _store.LightweightSession();

            foreach (var aggregate in aggregates)
            {
                var action = session.Events.Append(
                    aggregate.Id,
                    aggregate.Version,
                    aggregate.GetUncommittedEvents());

                foreach (var e in action.Events)
                {
                    _inlineEventsNotCatchedUpYet.TryAdd(e.Id, true);
                }
            }

            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        public object[] FetchStream(Guid streamId, long version = 0)
        {
            using var session = _store.LightweightSession();
            return session.Events.FetchStream(streamId, version).Select(e => e.Data).ToArray();
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

                var eventEnvelope = new EventEnvelope (
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

                var eventEnvelope = new EventEnvelope (
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
                    var eventEnvelope = new EventEnvelope (
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
                    var eventEnvelope = new EventEnvelope (
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

            string sql = $"SELECT MAX(seq_id) FROM {_store.Options.DatabaseSchemaName}.mt_events";
            using var conn = new NpgsqlConnection(_connectionString);
            using var cmd = new NpgsqlCommand(sql, conn);

            conn.Open();
            var result = cmd.ExecuteScalar();

            return (result is not null && result is not DBNull) ? (long)result : null;
        }

        public class Projection : Marten.Events.Projections.IProjection
        {
            private ProjectionRepository _projectionRepository;

            public Projection(ProjectionRepository projectionRepository)
            {
                _projectionRepository = projectionRepository;
            }

            public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
            {
                foreach (var stream in streams)
                {
                    var events = stream.Events.Select(e => new EventEnvelope(stream.Id, e.Id, e.Version, e.Sequence, e.Timestamp.UtcDateTime, e.Data)).ToList().AsReadOnly();
                    _projectionRepository.ApplyEvents(events);
                }
            }

            public async Task ApplyAsync(
                IDocumentOperations operations,
                IReadOnlyList<StreamAction> streams,
                CancellationToken cancellation)
            {
                foreach (var stream in streams)
                {
                    var events = stream.Events
                        .Select(
                            e =>
                            new EventEnvelope(
                                stream.Id,
                                e.Id,
                                e.Version,
                                e.Sequence,
                                e.Timestamp.UtcDateTime,
                                e.Data))
                        .ToList()
                        .AsReadOnly();

                    await _projectionRepository
                        .ApplyEventsAsync(events)
                        .ConfigureAwait(false);
                }
            }
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
    }
}
