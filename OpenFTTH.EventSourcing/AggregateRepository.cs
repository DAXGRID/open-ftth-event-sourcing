using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenFTTH.EventSourcing
{
    public sealed class AggregateRepository : IAggregateRepository
    {
        private readonly IEventStore store;

        public AggregateRepository(IEventStore store)
        {
            this.store = store;
        }

        public void Store(AggregateBase aggregate)
        {
            // Take non-persisted events and write them to the event store
            var events = aggregate.GetUncommittedEvents().ToArray();
            store.AppendStream(aggregate.Id, aggregate.Version, events);

            // Once succesfully persisted, clear events from list of uncommitted events
            aggregate.ClearUncommittedEvents();
        }

        public void StoreMany(IReadOnlyList<AggregateBase> aggregates)
        {
            store.AppendStream(aggregates);
            foreach (var aggregate in aggregates)
            {
                // Once succesfully persisted, clear events from list of uncommitted events
                aggregate.ClearUncommittedEvents();
            }
        }

        private static readonly MethodInfo ApplyEvent = typeof(AggregateBase).GetMethod("ApplyEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        public T Load<T>(Guid id, int? version = null) where T : AggregateBase
        {
            var events = store.FetchStream(id, version ?? 0);

            if (events != null && events.Any())
            {
                var instance = Activator.CreateInstance(typeof(T), true);

                ((T)instance).Id = id;

                // Replay our aggregate state from the event stream
                _ = events.Aggregate(instance, (o, @event) => ApplyEvent.Invoke(instance, new[] { @event }));
                return (T)instance;
            }
            else
            {
                // We create a new aggregate instance if no aggregate exists in database
                return (T)Activator.CreateInstance(typeof(T), true);
            }
        }

        public bool CheckIfAggregateIdHasBeenUsed(Guid id)
        {
            var events = store.FetchStream(id);

            if (events == null || (!events.Any()))
                return false;
            else
                return true;
        }
    }
}
