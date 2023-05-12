using Aocl;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        public void AppendStream(Guid streamId, long expectedVersion, object[] events)
        {
            List<IEventEnvelope> eventEnvelopes = new List<IEventEnvelope>();

            long version = expectedVersion;

            foreach (var @event in events)
            {
                version++;
                eventEnvelopes.Add(new EventEnvelope(streamId, Guid.Empty, version, 0, DateTime.UtcNow, @event));
            }

            AddEventsToStore(streamId, eventEnvelopes);

            _projectionRepository.ApplyEvents(eventEnvelopes);
        }

        public Task AppendStreamAsync(Guid streamId, long expectedVersion, object[] events)
        {
            // No async implementation just call default implementation.
            AppendStream(streamId, expectedVersion, events);
            return Task.CompletedTask;
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
                            DateTime.UtcNow,
                            @event));
                }

                AddEventsToStore(aggregate.Id, eventEnvelopes);

                _projectionRepository.ApplyEvents(eventEnvelopes);
            }
        }

        public Task AppendStreamAsync(IReadOnlyList<AggregateBase> aggregates)
        {
            // No async implementation just call default implementation.
            AppendStream(aggregates);
            return Task.CompletedTask;
        }

        private void AddEventsToStore(Guid streamId, List<IEventEnvelope> events)
        {
            var stream = _events.GetOrAdd(streamId, new AppendOnlyList<IEventEnvelope>());
            stream.AppendRange(events);
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

        public void DehydrateProjections()
        {
            // Do nothing for in memory
        }

        public long CatchUp()
        {
            // Do nothing for in memory
            return 0;
        }

        public Task DehydrateProjectionsAsync(CancellationToken cancellationToken = default)
        {
            // Do nothing for in memory
            return Task.CompletedTask;
        }

        public async Task<long> CatchUpAsync(CancellationToken cancellationToken = default)
        {
            // Do nothing for in memory
            return await Task.FromResult(0).ConfigureAwait(false);
        }

        public long? CurrentStreamVersion(Guid streamId)
        {
            // We -1 because we start at version 0.
            return (long)_events[streamId].Count() - 1;
        }

        public Task<long?> CurrentStreamVersionAsync(Guid streamId)
        {
            // We -1 because we start at version 0.
            return Task.FromResult((long?)_events[streamId].Count() - 1);
        }

        public void ScanForProjections()
        {
            _projectionRepository.ScanServiceProviderForProjections();
        }
    }
}
