using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public class ProjectionBase : IProjection
    {
        private readonly IDictionary<Type, MyEventHandler> _handlers = new ConcurrentDictionary<Type, MyEventHandler>();

        public void ProjectEvent<TEvent>(Action<IEventEnvelope> handler)
            where TEvent : class
        {
            var myHandler = new MyEventHandler()
            {
                Handler = (@event) => {
                    handler(@event as IEventEnvelope);
                    return Task.CompletedTask;
                }
            };

            _handlers.Add(typeof(TEvent), myHandler);
        }

        public void Apply(IReadOnlyList<IEventEnvelope> events)
        {
            foreach (var @event in events)
            {
                var eventType = @event.Data.GetType();

                if (_handlers.TryGetValue(eventType, out MyEventHandler handler))
                {
                    handler.Handler(@event);
                }
                else
                {
                    foreach (var handlerRegistered in _handlers)
                    {
                        if (eventType.IsSubclassOf(handlerRegistered.Key))
                            handlerRegistered.Value.Handler(@event);
                    }
                }
            }
        }

        public void Apply(IEventEnvelope @event)
        {
            var eventType = @event.Data.GetType();

            if (_handlers.TryGetValue(eventType, out MyEventHandler handler))
            {
                handler.Handler(@event);
            }
            else
            {
                foreach (var handlerRegistered in _handlers)
                {
                    if (eventType.IsSubclassOf(handlerRegistered.Key))
                        handlerRegistered.Value.Handler(@event);
                }
            }
        }

        private class MyEventHandler
        {
            public Func<object, Task> Handler { get; set; }
        }
    }
    
}
