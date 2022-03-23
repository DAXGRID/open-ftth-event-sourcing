using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenFTTH.EventSourcing
{
    public interface IProjection
    {
        void Apply(IReadOnlyList<IEventEnvelope> events);

        Task ApplyAsync(IReadOnlyList<IEventEnvelope> events);

        void Apply(IEventEnvelope @event);

        Task ApplyAsync(IEventEnvelope @event);

        void DehydrationFinish();
    }
}
