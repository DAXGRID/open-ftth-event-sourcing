using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenFTTH.EventSourcing.Tests.Model
{
    public class PoopProjection : ProjectionBase
    {
        private Dictionary<Guid, DogPoopStat> _poopStatByDogId = new Dictionary<Guid, DogPoopStat>();

        public List<DogPoopStat> PoopReport => _poopStatByDogId.Values.ToList();

        public PoopProjection()
        {
            ProjectEvent<DogBorn>(Project);
            ProjectEvent<DogPooped>(Project);
        }

        private void Project(IEventEnvelope eventEnvelope)
        {
            switch (eventEnvelope.Data)
            {
                case (DogBorn @event):
                    _poopStatByDogId[eventEnvelope.StreamId] = new DogPoopStat(@event.Name);
                    break;

                case (DogPooped @event):
                    _poopStatByDogId[eventEnvelope.StreamId].AddPoop(@event.WeightInGrams);
                    break;
            }
        }
    }

    public class DogPoopStat
    {
        public string DogName { get; }

        private int _poopTotal = 0;
        public int PoopTotal => _poopTotal;

        public DogPoopStat(string dogName)
        {
            this.DogName = dogName;
        }

        public void AddPoop(int poopWeightInGrams)
        {
            _poopTotal += poopWeightInGrams;
        }
    }
}
