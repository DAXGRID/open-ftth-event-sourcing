using System;

namespace OpenFTTH.EventSourcing
{
    public interface IEventEnvelope
    {
        Guid StreamId { get; }
        Type EventType { get; }
        long Version { get; }
        object Data { get; }
    }
}