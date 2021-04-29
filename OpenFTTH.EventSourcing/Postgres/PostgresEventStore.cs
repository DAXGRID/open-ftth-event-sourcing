using Marten;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Storage;
using System;
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

        private ProjectionRepository _projectionRepository;
        public IProjectionRepository Projections => _projectionRepository;

        private AggregateRepository _aggregateRepository;
        public IAggregateRepository Aggregates => _aggregateRepository;

        private ICommandLog _commandLog;
        public ICommandLog CommandLog => _commandLog;

        public PostgresEventStore(IServiceProvider serviceProvider, string connectionString, string databaseSchemaName, bool cleanAll = false)
        {
            _aggregateRepository = new AggregateRepository(this);
            _projectionRepository = new ProjectionRepository(serviceProvider);

            var options = new StoreOptions();
            options.Connection(connectionString);
            options.Events.Projections.Add(new Projection(_projectionRepository));

            // Can be overridden
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.DatabaseSchemaName = databaseSchemaName;

            _store = new DocumentStore(options);

            if (cleanAll)
                _store.Advanced.Clean.CompletelyRemoveAll();

            _commandLog = new PostgresCommandLog(_store);
        }

        public void Store(AggregateBase aggregate)
        {
            using (var session = _store.OpenSession())
            {
                // Take non-persisted events, push them to the event stream, indexed by the aggregate ID
                var events = aggregate.GetUncommittedEvents().ToArray();
                session.Events.Append(aggregate.Id, aggregate.Version, events);
                session.SaveChanges();
            }
            // Once succesfully persisted, clear events from list of uncommitted events
            aggregate.ClearUncommittedEvents();
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
            session.Events.Append(streamId, expectedVersion, events);
            session.SaveChanges();
        }

        public object[] FetchStream(Guid streamId, int version = 0)
        {
            using var session = _store.LightweightSession();
            return session.Events.FetchStream(streamId, version).Select(e => e.Data).ToArray();
        }

        public void DehydrateProjections()
        {
            using var session = _store.LightweightSession();
            foreach (var martenEvent in session.Events.QueryAllRawEvents().OrderBy(e => e.Sequence))
            {
                _projectionRepository.ApplyEvent(new EventEnvelope(martenEvent.StreamId, martenEvent.Id, martenEvent.Version, martenEvent.Sequence, martenEvent.Data));
            }
        }

        public class Projection : Marten.Events.Projections.IProjection
        {
            private ProjectionRepository _projectionRepository;
            public Guid Id { get; set; }

            public string Name { get; set; }

            public Projection(ProjectionRepository projectionRepository)
            {
                _projectionRepository = projectionRepository;
            }


            public void EnsureStorageExists(ITenant tenant)
            {
                //tenant.EnsureStorageExists(typeof(QuestPatchTestProjection));
            }

            public Type[] Consumes { get; } = new Type[] { /*typeof(SomethingHappend)*/ };

            public AsyncOptions AsyncOptions { get; } = new AsyncOptions();


            public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
            {
                foreach (var stream in streams)
                {
                    var events = stream.Events.Select(e => new EventEnvelope(stream.Id, e.Id, e.Version, e.Sequence, e.Data)).ToList().AsReadOnly();
                    _projectionRepository.ApplyEvents(events);
                }
            }

            public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
            {
                return Task.CompletedTask;
            }
        }

    }
}
