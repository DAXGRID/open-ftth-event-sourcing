using FluentAssertions;
using OpenFTTH.EventSourcing.InMem;
using OpenFTTH.EventSourcing.Tests.Model;
using System;
using Xunit;

namespace OpenFTTH.EventSourcing.Tests
{
    public class InMemEventStoreTests
    {
        [Fact]
        public void TestRawEventAppendingAndFetching()
        {
            var eventStore = new InMemEventStore() as IEventStore;

            var streamId = Guid.NewGuid();
            var eventsToSave = new object[] { new DogBorn("Snoopy"), new DogBorn("Pluto") };
            eventStore.AppendStream(streamId, 0, eventsToSave);

            var eventsFetched = eventStore.FetchStream(streamId);
            eventsFetched.Should().BeEquivalentTo(eventsToSave);
        }

        [Fact]
        public void TestAggregateHydrationAndDehydration()
        {
            var eventStore = new InMemEventStore() as IEventStore;

            var aggregateBeforeHydration = new DogAggregate(Guid.NewGuid(), "Snoopy");
            aggregateBeforeHydration.Bark(100);
            aggregateBeforeHydration.Poop(1500);

            eventStore.Aggregates.Store(aggregateBeforeHydration);

            var dehydratedAggregate = eventStore.Aggregates.Load<DogAggregate>(aggregateBeforeHydration.Id);

            dehydratedAggregate.Should().BeEquivalentTo(aggregateBeforeHydration);
        }


        [Fact]
        public void TestProjection()
        {
            var eventStore = new InMemEventStore() as IEventStore;

            var poopProjection = new PoopProjection();

            eventStore.Projections.Add(poopProjection);

            var snoopy = new DogAggregate(Guid.NewGuid(), "Snoopy");
            snoopy.Poop(200);
            snoopy.Poop(700);
            snoopy.Poop(50);
            eventStore.Aggregates.Store(snoopy);

            var pluto = new DogAggregate(Guid.NewGuid(), "Pluto");
            pluto.Poop(2000);
            pluto.Poop(100);
            eventStore.Aggregates.Store(pluto);

            poopProjection.PoopReport.Count.Should().Be(2);
            poopProjection.PoopReport.Find(p => p.DogName == "Snoopy").PoopTotal.Should().Be(950);
            poopProjection.PoopReport.Find(p => p.DogName == "Pluto").PoopTotal.Should().Be(2100);
        }

        [Fact]
        public void TestLoadAggregateThatDontExitInEventStore_ShouldReturnNewBlankInstance()
        {
            var eventStore = new InMemEventStore() as IEventStore;

            Guid newDogId = Guid.NewGuid();
  
            var newDog = eventStore.Aggregates.Load<DogAggregate>(newDogId);

            newDog.Version.Should().Be(0);
        }
    }
}
