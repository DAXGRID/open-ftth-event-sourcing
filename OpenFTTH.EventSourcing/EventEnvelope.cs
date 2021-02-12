using System;

namespace OpenFTTH.EventSourcing
{
    public class EventEnvelope : IEventEnvelope
    {
        public Guid StreamId { get; }
        public Type EventType { get; }
        public long Version { get; }
        public object Data { get; }

        public EventEnvelope(Guid streamId, long version, object data)
        {
            this.StreamId = streamId;
            this.Version = version;
            this.Data = data;
            this.EventType = data.GetType();
        }
    }
}
