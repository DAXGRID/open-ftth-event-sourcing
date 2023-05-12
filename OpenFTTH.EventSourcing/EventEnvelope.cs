using System;

namespace OpenFTTH.EventSourcing
{
    public class EventEnvelope : IEventEnvelope
    {
        public Guid StreamId { get; }
        public Guid EventId { get; }
        public long Version { get; }
        public long GlobalVersion { get; }
        public DateTime EventTimeStamp { get; init; }
        public object Data { get; }

        public EventEnvelope(Guid streamId, Guid eventId, long version, long globalVersion, DateTime eventTimeStamp, object data)
        {
            StreamId = streamId;
            EventId = eventId;
            Version = version;
            GlobalVersion = globalVersion;
            Data = data;
        }
    }
}
