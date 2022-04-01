using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public class ProjectionBase : IProjection
    {
        private readonly IDictionary<Type, MyEventHandler> _handlers = new ConcurrentDictionary<Type, MyEventHandler>();

        public List<Type> GetHandlerEventTypes()
            => _handlers.Keys.ToList();

        public void ProjectEvent<TEvent>(Action<IEventEnvelope> handler)
            where TEvent : class
        {
            var myHandler = new MyEventHandler()
            {
                Handler = (@event) =>
                {
                    handler(@event as IEventEnvelope);
                    return Task.CompletedTask;
                }
            };

            _handlers.Add(typeof(TEvent), myHandler);
        }

        public void ProjectEventAsync<TEvent>(Func<IEventEnvelope, Task> handler)
            where TEvent : class
        {
            var myHandler = new MyEventHandler()
            {
                Handler = async (@event) =>
               {
                   await handler(@event as IEventEnvelope);
               }
            };

            _handlers.Add(typeof(TEvent), myHandler);
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

        public async Task ApplyAsync(IEventEnvelope @event)
        {
            var eventType = @event.Data.GetType();

            if (_handlers.TryGetValue(eventType, out MyEventHandler handler))
            {
                await handler.Handler(@event).ConfigureAwait(false);
            }
            else
            {
                foreach (var handlerRegistered in _handlers)
                {
                    if (eventType.IsSubclassOf(handlerRegistered.Key))
                        await handlerRegistered.Value.Handler(@event).ConfigureAwait(false);
                }
            }
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

        public async Task ApplyAsync(IReadOnlyList<IEventEnvelope> events)
        {
            foreach (var @event in events)
            {
                var eventType = @event.Data.GetType();

                if (_handlers.TryGetValue(eventType, out MyEventHandler handler))
                {
                    await handler.Handler(@event).ConfigureAwait(false);
                }
                else
                {
                    foreach (var handlerRegistered in _handlers)
                    {
                        if (eventType.IsSubclassOf(handlerRegistered.Key))
                            await handlerRegistered.Value.Handler(@event).ConfigureAwait(false);
                    }
                }
            }
        }

        private class MyEventHandler
        {
            public Func<object, Task> Handler { get; set; }
        }

        public virtual void DehydrationFinish()
        {

        }

        public virtual async Task DehydrationFinishAsync()
        {
            await Task.CompletedTask;
        }
    }
}
