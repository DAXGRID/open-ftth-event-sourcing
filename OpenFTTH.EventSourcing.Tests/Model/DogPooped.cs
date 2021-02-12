namespace OpenFTTH.EventSourcing.Tests.Model
{
    public class DogPooped
    {
        public int WeightInGrams { get; }

        public DogPooped(int weightInGrams)
        {
            this.WeightInGrams = weightInGrams;
        }
    }
}
