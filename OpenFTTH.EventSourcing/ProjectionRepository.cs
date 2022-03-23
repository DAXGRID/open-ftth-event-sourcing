using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public class ProjectionRepository : IProjectionRepository
    {
        private readonly IServiceProvider _serviceProvider;

        private bool _serviceProviderHasBeenScanned = false;

        private readonly ConcurrentBag<IProjection> _projections = new ConcurrentBag<IProjection>();

        public ProjectionRepository(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Add(IProjection projection)
        {
            _projections.Add(projection);
        }

        internal void ApplyEvents(IReadOnlyList<IEventEnvelope> events)
        {
            ScanServiceProviderForProjections();

            foreach (var projection in _projections)
            {
                projection.Apply(events);
            }
        }

        internal async Task ApplyEventsAsync(IReadOnlyList<IEventEnvelope> events)
        {
            ScanServiceProviderForProjections();

            foreach (var projection in _projections)
            {
                await projection.ApplyAsync(events).ConfigureAwait(false);
            }
        }

        internal void ApplyEvent(IEventEnvelope @event)
        {
            ScanServiceProviderForProjections();

            foreach (var projection in _projections)
            {
                projection.Apply(@event);
            }
        }

        internal async Task ApplyEventAsync(IEventEnvelope @event)
        {
            ScanServiceProviderForProjections();

            foreach (var projection in _projections)
            {
                await projection.ApplyAsync(@event).ConfigureAwait(false);
            }
        }

        private void ScanServiceProviderForProjections()
        {
            if (_serviceProvider != null && !_serviceProviderHasBeenScanned)
            {
                var projections = _serviceProvider.GetServices<IProjection>();

                if (projections != null)
                {
                    foreach (var projection in projections)
                    {
                        if (!_projections.Any(existingProjection => existingProjection.GetType() == projection.GetType()))
                            _projections.Add(projection);
                    }
                }

                _serviceProviderHasBeenScanned = true;
            }
        }

        public T Get<T>()
        {
            ScanServiceProviderForProjections();

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
    }
}
