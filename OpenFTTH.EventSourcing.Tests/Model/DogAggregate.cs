using System;

namespace OpenFTTH.EventSourcing.Tests.Model
{
    public class DogAggregate : AggregateBase
    {
        private string _name;
        public string Name => _name;

        public int _numberOfPoopsInLifetime;
        public int NumberOfPoopsInLifetime => _numberOfPoopsInLifetime;

        public DateTime _lastTimeBarked;
        public DateTime LastTimeBarked => _lastTimeBarked;


        public DogAggregate()
        {
            Register<DogBorn>(Apply);
            Register<DogBarked>(Apply);
            Register<DogPooped>(Apply);
        }

        public DogAggregate(Guid id, string name) : this()
        {
            this.Id = id;
            RaiseEvent(new DogBorn(name));
        }

        public void Bark(int volumneInDb)
        {
            RaiseEvent(new DogBarked(volumneInDb, DateTime.Now));
        }

        public void Poop(int weightInGrams)
        {
            RaiseEvent(new DogPooped(weightInGrams));
        }

        private void Apply(DogBorn @event)
        {
            _name = @event.Name;
        }

        private void Apply(DogBarked @event)
        {
            _lastTimeBarked = @event.BarkTimestamp;
        }

        private void Apply(DogPooped @event)
        {
            _numberOfPoopsInLifetime++;
        }
    }
}
