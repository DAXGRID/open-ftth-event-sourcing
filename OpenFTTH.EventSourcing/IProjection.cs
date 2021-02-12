using System.Collections.Generic;

namespace OpenFTTH.EventSourcing
{
    public interface IProjection
    {
        void Apply(IReadOnlyList<IEventEnvelope> events);
    }
}