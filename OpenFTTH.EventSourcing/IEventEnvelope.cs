using System;

namespace OpenFTTH.EventSourcing
{
    public interface IEventEnvelope
    {
        public Guid StreamId { get; }
        public Guid EventId { get; }
        public long Version { get; }
        public long GlobalVersion { get; }
        object Data { get; }
    }
}