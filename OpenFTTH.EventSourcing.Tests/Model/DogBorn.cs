namespace OpenFTTH.EventSourcing.Tests.Model
{
    public class DogBorn
    {
        public string Name { get; }

        public DogBorn(string name)
        {
            this.Name = name;
        }
    }
}
