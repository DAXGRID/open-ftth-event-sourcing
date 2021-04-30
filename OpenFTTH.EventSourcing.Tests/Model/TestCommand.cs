using System;

namespace OpenFTTH.EventSourcing.Tests.Model
{
    public record TestCommand
    {
        public Guid CmdId { get; set; }
        public string Name { get; set; }
        public int Weight { get; set; }
        public Gender Gender { get; set; }
    }

    public enum Gender
    {
        Male,
        Female
    }
}
