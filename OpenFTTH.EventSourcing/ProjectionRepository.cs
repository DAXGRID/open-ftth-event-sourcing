using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public class ProjectionRepository : IProjectionRepository
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentBag<IProjection> _projections = new ConcurrentBag<IProjection>();

        public ProjectionRepository(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Add(IProjection projection)
        {
            _projections.Add(projection);
        }

        internal List<IProjection> GetAll()
            => _projections.ToList();

        internal void ApplyEvents(IReadOnlyList<IEventEnvelope> events)
        {
            foreach (var projection in _projections)
            {
                projection.Apply(events);
            }
        }

        internal async Task ApplyEventsAsync(IReadOnlyList<IEventEnvelope> events)
        {
            foreach (var projection in _projections)
            {
                await projection.ApplyAsync(events).ConfigureAwait(false);
            }
        }

        internal void ApplyEvent(IEventEnvelope @event)
        {
            foreach (var projection in _projections)
            {
                projection.Apply(@event);
            }
        }

        internal async Task ApplyEventAsync(IEventEnvelope @event)
        {
            foreach (var projection in _projections)
            {
                await projection.ApplyAsync(@event).ConfigureAwait(false);
            }
        }

        public void ScanServiceProviderForProjections()
        {
            if (_serviceProvider is null)
            {
                throw new InvalidOperationException(
                    "Service provider is required to scan for projections.");
            }

            var projections = _serviceProvider.GetServices<IProjection>() ??
                throw new InvalidOperationException(
                    $"Could not resolve any services implementing '{nameof(IProjection)}'.");

            foreach (var projection in projections)
            {
                var containsProjection = _projections
                    .Any(existingProjection =>
                         existingProjection.GetType() == projection.GetType());

                if (!containsProjection)
                {
                    _projections.Add(projection);
                }
            }
        }

        public T Get<T>()
        {
            foreach (var projection in _projections)
            {
                if (projection is T)
                    return (T)projection;
            }

            throw new ArgumentException($"Cant find projection of type: {typeof(T).Name}");
        }

        internal void DehydrationFinish()
        {
            foreach (var projection in _projections)
            {
                projection.DehydrationFinish();
            }
        }

        internal async Task DehydrationFinishAsync()
        {
            foreach (var projection in _projections)
            {
                await projection.DehydrationFinishAsync().ConfigureAwait(false);
            }
        }
    }
}
