using Marten;
using Marten.Events;
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

        private ICommandLog _commandLog;
        public ICommandLog CommandLog => _commandLog;

        private ISequences _sequences;
        public ISequences Sequences => _sequences;


        public PostgresEventStore(IServiceProvider serviceProvider, string connectionString, string databaseSchemaName, bool cleanAll = false)
        {
            _aggregateRepository = new AggregateRepository(this);
            _projectionRepository = new ProjectionRepository(serviceProvider);

            var options = new StoreOptions();
            options.Connection(connectionString);
            options.Projections.Add(new Projection(_projectionRepository));

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

            _commandLog = new PostgresCommandLog(_store);

            _sequences = new PostgresSequenceStore(connectionString, databaseSchemaName);
        }

        private static readonly MethodInfo ApplyEvent = typeof(AggregateBase).GetMethod("ApplyEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        public T Load<T>(Guid id, int? version = null) where T : AggregateBase
        {
            IReadOnlyList<IEvent> events;
            using (var session = _store.LightweightSession())
            {
                events = session.Events.FetchStream(id, version ?? 0);
            }

            if (events != null && events.Any())
            {
                var instance = Activator.CreateInstance(typeof(T), true);
                // Replay our aggregate state from the event stream
                events.Aggregate(instance, (o, @event) => ApplyEvent.Invoke(instance, new[] { @event.Data }));
                return (T)instance;
            }

            throw new InvalidOperationException($"No aggregate by id {id}.");
        }

        public bool CheckIfAggregateIdHasBeenUsed(Guid id)
        {
            IReadOnlyList<IEvent> events;
            using (var session = _store.LightweightSession())
            {
                events = session.Events.FetchStream(id);
            }

            if (events == null || (!events.Any()))
                return false;
            else
                return true;
        }

        public void AppendStream(Guid streamId, int expectedVersion, object[] events)
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

        public async Task AppendStreamAsync(Guid streamId, int expectedVersion, object[] events)
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

        public object[] FetchStream(Guid streamId, int version = 0)
        {
            using var session = _store.LightweightSession();
            return session.Events.FetchStream(streamId, version).Select(e => e.Data).ToArray();
        }

        public void DehydrateProjections()
        {
            using var session = _store.LightweightSession();

            var eventTypes = GetMartenDotNetTypeFormat(_projectionRepository.GetAll());
            var events = session.Events.QueryAllRawEvents()
                .Where(x => x.DotNetTypeName.IsOneOf(eventTypes))
                .OrderBy(e => e.Sequence);

            foreach (var martenEvent in events)
            {
                _projectionRepository.ApplyEvent(new EventEnvelope(martenEvent.StreamId, martenEvent.Id, martenEvent.Version, martenEvent.Sequence, martenEvent.Data));

                _lastSequenceNumberProcessed = martenEvent.Sequence;
            }

            _projectionRepository.DehydrationFinish();
        }

        public async Task DehydrateProjectionsAsync(CancellationToken cancellationToken = default)
        {
            await using var session = _store.LightweightSession();

            var eventTypes = GetMartenDotNetTypeFormat(_projectionRepository.GetAll());
            var events = session.Events.QueryAllRawEvents()
                .Where(x => x.DotNetTypeName.IsOneOf(eventTypes))
                .OrderBy(e => e.Sequence)
                .ToAsyncEnumerable(cancellationToken)
                .ConfigureAwait(false);

            await foreach (var martenEvent in events)
            {
                await _projectionRepository
                    .ApplyEventAsync(
                        new EventEnvelope(
                            martenEvent.StreamId,
                            martenEvent.Id,
                            martenEvent.Version,
                            martenEvent.Sequence,
                            martenEvent.Data))
                    .ConfigureAwait(false);

                _lastSequenceNumberProcessed = martenEvent.Sequence;
            }

            await _projectionRepository.DehydrationFinishAsync().ConfigureAwait(false);
        }

        public long CatchUp()
        {
            using var session = _store.LightweightSession();

            long eventsProcessed = 0;
            var eventTypes = GetMartenDotNetTypeFormat(_projectionRepository.GetAll());
            var events = session.Events.QueryAllRawEvents()
                .Where(e => e.Sequence > _lastSequenceNumberProcessed && e.DotNetTypeName.IsOneOf(eventTypes))
                .OrderBy(e => e.Sequence);

            foreach (var martenEvent in events)
            {
                eventsProcessed++;

                if (_inlineEventsNotCatchedUpYet.ContainsKey(martenEvent.Id))
                {
                    // Do nothing but remove the event id from the inline event dictionary to free up memory
                    _inlineEventsNotCatchedUpYet.TryRemove(martenEvent.Id, out var _);
                }
                else
                {
                    // Because the event id don't exist in the inline event dictionary, it must be an external event that has to be applied to projectionsd
                    _projectionRepository.ApplyEvent(new EventEnvelope(martenEvent.StreamId, martenEvent.Id, martenEvent.Version, martenEvent.Sequence, martenEvent.Data));
                }

                _lastSequenceNumberProcessed = martenEvent.Sequence;
            }

            return eventsProcessed;
        }

        public async Task<long> CatchUpAsync(CancellationToken cancellationToken = default)
        {
            await using var session = _store.LightweightSession();

            var eventTypes = GetMartenDotNetTypeFormat(_projectionRepository.GetAll());
            var events = session.Events.QueryAllRawEvents()
                .Where(e => e.Sequence > _lastSequenceNumberProcessed && e.DotNetTypeName.IsOneOf(eventTypes))
                .OrderBy(e => e.Sequence)
                .ToAsyncEnumerable(cancellationToken)
                .ConfigureAwait(false);

            long eventsProcessed = 0;

            await foreach (var martenEvent in events)
            {
                eventsProcessed++;

                if (_inlineEventsNotCatchedUpYet.ContainsKey(martenEvent.Id))
                {
                    // Do nothing but remove the event id from the inline event dictionary to free up memory
                    _inlineEventsNotCatchedUpYet.TryRemove(martenEvent.Id, out var _);
                }
                else
                {
                    // Because the event id don't exist in the inline event dictionary, it must be an external event that has to be applied to projections
                    await _projectionRepository
                        .ApplyEventAsync(
                            new EventEnvelope(
                                martenEvent.StreamId,
                                martenEvent.Id,
                                martenEvent.Version,
                                martenEvent.Sequence,
                                martenEvent.Data))
                        .ConfigureAwait(false);
                }

                _lastSequenceNumberProcessed = martenEvent.Sequence;
            }

            return eventsProcessed;
        }

        private static List<string> GetMartenDotNetTypeFormat(List<IProjection> projections)
            =>
            projections
            .SelectMany(x => x.GetHandlerEventTypes())
            .Select(x => $"{x.FullName}, {x.Assembly.GetName().Name}")
            .Distinct() // We Distinct to remove all duplicates
            .ToList();

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
                    var events = stream.Events.Select(e => new EventEnvelope(stream.Id, e.Id, e.Version, e.Sequence, e.Data)).ToList().AsReadOnly();
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
                                e.Data))
                        .ToList()
                        .AsReadOnly();

                    await _projectionRepository
                        .ApplyEventsAsync(events)
                        .ConfigureAwait(false);
                }
            }
        }
    }
}
