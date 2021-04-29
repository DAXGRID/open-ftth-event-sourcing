using OpenFTTH.CQRS;

namespace OpenFTTH.EventSourcing.Tests.Model
{
    public record TestCommand : BaseCommand
    {
        public string Name { get; set; }
        public int Weight { get; set; }
    }
}
