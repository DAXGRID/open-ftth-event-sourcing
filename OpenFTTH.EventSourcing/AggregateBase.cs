using System;
using System.Collections.Generic;

namespace OpenFTTH.EventSourcing
{
    public abstract class AggregateBase
    {
        // For indexing our event streams
        public Guid Id { get; internal protected set; }

        // For protecting the state, i.e. conflict prevention
        public int Version { get; protected set; }

        private readonly List<object> uncommittedEvents = new List<object>();
        private readonly Dictionary<Type, Action<object>> handlers = new Dictionary<Type, Action<object>>();

        // Get the deltas, i.e. events that make up the state, not yet persisted
        public IEnumerable<object> GetUncommittedEvents()
        {
            return uncommittedEvents;
        }

        // Mark the deltas as persisted.
        public void ClearUncommittedEvents()
        {
            uncommittedEvents.Clear();
        }

        // Infrastructure for raising events & registering handlers

        protected void Register<T>(Action<T> handle)
        {
            handlers[typeof(T)] = e => handle((T)e);
        }

        protected void RaiseEvent(object @event, bool applyEventToAggregate = true)
        {
            if (applyEventToAggregate)
                ApplyEvent(@event);

            uncommittedEvents.Add(@event);
        }

        private void ApplyEvent(object @event)
        {
            handlers[@event.GetType()](@event);
            // Each event bumps our version
            Version++;
        }
    }
}
