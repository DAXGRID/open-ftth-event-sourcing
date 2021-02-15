using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace OpenFTTH.EventSourcing.InMem
{
    public class InMemEventStore : IEventStore
    {
        private readonly ConcurrentDictionary<Guid, List<IEventEnvelope>> _events = new ConcurrentDictionary<Guid, List<IEventEnvelope>>();

        private ProjectionRepository _projectionRepository;
        public IProjectionRepository Projections => _projectionRepository;

        private AggregateRepository _aggregateRepository;
        public IAggregateRepository Aggregates => _aggregateRepository;

        public InMemEventStore(IServiceProvider serviceProvider)
        {
            _aggregateRepository = new AggregateRepository(this);

            _projectionRepository = new ProjectionRepository(serviceProvider);
        }

        public void AppendStream(Guid streamId, long expectedVersion, object[] events)
        {
            List<IEventEnvelope> eventEnvelopes = new List<IEventEnvelope>();

            long version = expectedVersion;

            foreach (var @event in events)
            {
                version++;
                eventEnvelopes.Add(new EventEnvelope(streamId, version, @event));
            }

            AddEventsToStore(streamId, eventEnvelopes);

            _projectionRepository.ApplyEvents(eventEnvelopes);
        }

        private void AddEventsToStore(Guid streamId, List<IEventEnvelope> events)
        {
            _events.AddOrUpdate(streamId, events, (streamId, existingStreamEvents) => 
                {
                    var newEventList = new List<IEventEnvelope>();
                    newEventList.AddRange(existingStreamEvents);
                    newEventList.AddRange(events);
                    return newEventList;
                }
            );
        }

        public object[] FetchStream(Guid streamId, long version = 0)
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
    }
}
