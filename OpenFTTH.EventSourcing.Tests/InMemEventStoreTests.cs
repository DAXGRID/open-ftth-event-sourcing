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

            eventStore.AggregateRepository.Store(aggregateBeforeHydration);

            var dehydratedAggregate = eventStore.AggregateRepository.Load<DogAggregate>(aggregateBeforeHydration.Id);

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
            eventStore.AggregateRepository.Store(snoopy);

            var pluto = new DogAggregate(Guid.NewGuid(), "Pluto");
            pluto.Poop(2000);
            pluto.Poop(100);
            eventStore.AggregateRepository.Store(pluto);

            poopProjection.PoopReport.Count.Should().Be(2);
            poopProjection.PoopReport.Find(p => p.DogName == "Snoopy").PoopTotal.Should().Be(950);
            poopProjection.PoopReport.Find(p => p.DogName == "Pluto").PoopTotal.Should().Be(2100);
        }
    }
}
