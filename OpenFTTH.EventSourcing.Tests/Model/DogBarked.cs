using System;

namespace OpenFTTH.EventSourcing.Tests.Model
{
    public class DogBarked
    {
        public DateTime BarkTimestamp { get; }

        public int VolumeInDb { get; }

        public DogBarked(int volumeInDb)
        {
            BarkTimestamp = DateTime.UtcNow;
            this.VolumeInDb = volumeInDb;
        }
    }
}
