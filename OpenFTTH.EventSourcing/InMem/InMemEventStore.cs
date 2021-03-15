using Aocl;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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

        public InMemEventStore(IServiceProvider serviceProvider)
        {
            _aggregateRepository = new AggregateRepository(this);

            _projectionRepository = new ProjectionRepository(serviceProvider);
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
            throw new NotImplementedException();
        }
    }
}
