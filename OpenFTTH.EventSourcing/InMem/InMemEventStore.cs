using Aocl;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing.InMem
{
    /// <summary>
    /// Should be used for testing only.
    /// </summary>
    public class InMemEventStore : IEventStore
    {
        private readonly ConcurrentDictionary<Guid, AppendOnlyList<IEventEnvelope>> _events = new ConcurrentDictionary<Guid, AppendOnlyList<IEventEnvelope>>();

        private ProjectionRepository _projectionRepository;
        public IProjectionRepository Projections => _projectionRepository;

        private AggregateRepository _aggregateRepository;
        public IAggregateRepository Aggregates => _aggregateRepository;

        private ICommandLog _commandLog;
        public ICommandLog CommandLog => _commandLog;

        private ISequences _sequences;
        public ISequences Sequences => _sequences;

        public InMemEventStore(IServiceProvider serviceProvider)
        {
            _aggregateRepository = new AggregateRepository(this);
            _projectionRepository = new ProjectionRepository(serviceProvider);
            _commandLog = new InMemCommandLog();
            _sequences = new InMemSequenceStore();
        }

        public void AppendStream(Guid streamId, int expectedVersion, object[] events)
        {
            List<IEventEnvelope> eventEnvelopes = new List<IEventEnvelope>();

            long version = expectedVersion;

            foreach (var @event in events)
            {
                version++;
                eventEnvelopes.Add(new EventEnvelope(streamId, Guid.Empty, version, 0, @event));
            }

            AddEventsToStore(streamId, eventEnvelopes);

            _projectionRepository.ApplyEvents(eventEnvelopes);
        }

        public void AppendStream(IReadOnlyList<AggregateBase> aggregates)
        {
            foreach (var aggregate in aggregates)
            {
                var eventEnvelopes = new List<IEventEnvelope>();
                var version = aggregate.Version;
                foreach (var @event in aggregate.GetUncommittedEvents())
                {
                    version++;
                    eventEnvelopes.Add(
                        new EventEnvelope(
                            aggregate.Id,
                            Guid.Empty,
                            version,
                            0,
                            @event));
                }

                AddEventsToStore(aggregate.Id, eventEnvelopes);

                _projectionRepository.ApplyEvents(eventEnvelopes);
            }
        }

        private void AddEventsToStore(Guid streamId, List<IEventEnvelope> events)
        {
            var stream = _events.GetOrAdd(streamId, new AppendOnlyList<IEventEnvelope>());

            stream.AppendRange(events);
        }

        public object[] FetchStream(Guid streamId, int version = 0)
        {
            if (!_events.ContainsKey(streamId))
            {
                return null;
            }
            else
            {
                return _events[streamId].Select(p => p.Data).ToArray();
            }
        }

        public void DehydrateProjections()
        {
            // Do nothing for in memory
        }

        public long CatchUp()
        {
            // Do nothing for in memory
            return 0;
        }

        public Task DehydrateProjectionsAsync()
        {
            // Do nothing for in memory
            return Task.CompletedTask;
        }

        public async Task<long> CatchUpAsync()
        {
            // Do nothing for in memory
            return await Task.FromResult(0);
        }
    }
}
