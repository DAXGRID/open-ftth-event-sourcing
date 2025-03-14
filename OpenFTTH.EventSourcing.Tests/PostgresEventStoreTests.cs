using FluentAssertions;
using FluentResults;
using OpenFTTH.EventSourcing.InMem;
using OpenFTTH.EventSourcing.Postgres;
using OpenFTTH.EventSourcing.Tests.Model;
using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace OpenFTTH.EventSourcing.Tests
{
    public class PostgresEventStoreTests
    {
        private static string _connectionString = Environment.GetEnvironmentVariable("test_event_store_connection");

        [Fact]
        public void TestRawEventAppendingAndFetching()
        {
            if (_connectionString == null)
                return;

            var eventStore = new PostgresEventStore(null, _connectionString, "TestRawEventAppendingAndFetching", true) as IEventStore;

            var streamId = Guid.NewGuid();
            var eventsToSave = new object[] { new DogBorn("Snoopy"), new DogBorn("Pluto") };
            eventStore.AppendStream(streamId, 0, eventsToSave);

            var eventsFetched = eventStore.FetchStream(streamId);
            eventsFetched.Should().BeEquivalentTo(eventsToSave);
        }

        [Fact]
        public void TestAggregateHydrationAndDehydration()
        {
            if (_connectionString == null)
                return;

            var eventStore = new PostgresEventStore(null, _connectionString, "TestAggregateHydrationAndDehydration", true) as IEventStore;

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
            if (_connectionString == null)
                return;

            var eventStore = new PostgresEventStore(null, _connectionString, "TestProjection", true) as IEventStore;

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
            if (_connectionString == null)
                return;

            var eventStore = new InMemEventStore(null) as IEventStore;

            Guid newDogId = Guid.NewGuid();

            var newDog = eventStore.Aggregates.Load<DogAggregate>(newDogId);

            newDog.Version.Should().Be(0);
        }


        [Fact]
        public void TestDehydrateProjections()
        {
            if (_connectionString == null)
                return;

            // Create events store, put some events in
            var eventStore = new PostgresEventStore(null, _connectionString, "TestDehydrateProjections", true) as IEventStore;


            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < 1000; i++)
            {
                var dog = new DogAggregate(Guid.NewGuid(), "Dog name");
                dog.Poop(2000);
                dog.Poop(1030);
                dog.Poop(1020);
                dog.Poop(1100);
                dog.Bark(1020);
                dog.Bark(1040);
                dog.Poop(1200);
                dog.Poop(1300);
                dog.Bark(1040);
                dog.Poop(1200);
                eventStore.Aggregates.Store(dog);
            }

            sw.Stop();
            System.Diagnostics.Debug.WriteLine("Create 10000 events took: " + (sw.ElapsedMilliseconds) + " milliseconds");

            // Create new instance of event store and dehydrate into in-memory projection

            var eventStore2 = new PostgresEventStore(null, _connectionString, "TestDehydrateProjections") as IEventStore;

            var poopProjection = new PoopProjection();

            eventStore.Projections.Add(poopProjection);

            sw.Restart();

            eventStore.DehydrateProjections();

            sw.Stop();
            System.Diagnostics.Debug.WriteLine("Dehydrate 10000 events took: " + (sw.ElapsedMilliseconds) + " milliseconds");

            poopProjection.PoopReport.Count.Should().Be(1000);
        }

        [Fact]
        public void SequenceTest()
        {
            if (_connectionString == null)
                return;

            var eventStore = new PostgresEventStore(null, _connectionString, "TestSequences", true) as IEventStore;

            eventStore.Sequences.DropSequence("seq1");
            eventStore.Sequences.DropSequence("seq2");

            var seq1val = eventStore.Sequences.GetNextVal("seq1");
            seq1val.Should().Be(1);

            var seq2val = eventStore.Sequences.GetNextVal("seq2");
            seq1val.Should().Be(1);

            // bump seq1
            seq1val = eventStore.Sequences.GetNextVal("seq1");
            seq1val.Should().Be(2);
            seq2val.Should().Be(1);

            // bump seq2
            seq2val = eventStore.Sequences.GetNextVal("seq2");
            seq1val.Should().Be(2);
            seq2val.Should().Be(2);
        }

        [Fact]
        public void TestCatchup()
        {
            if (_connectionString == null)
                return;

            var eventStore = new PostgresEventStore(null, _connectionString, "TestProjection", true) as IEventStore;

            var poopProjection = new PoopProjection();

            eventStore.Projections.Add(poopProjection);

            var snoopyId = Guid.NewGuid();

            var snoopy = new DogAggregate(snoopyId, "Snoopy");
            snoopy.Poop(100);
            snoopy.Poop(100);
            snoopy.Poop(100);
            eventStore.Aggregates.Store(snoopy);

            ((PostgresEventStore)eventStore).NumberOfInlineEventsNotCatchedUp.Should().Be(4);

            poopProjection.PoopReport.First().PoopTotal.Should().Be(300);

            // Open new event store and dehydrate events
            var eventStore2 = new PostgresEventStore(null, _connectionString, "TestProjection", false) as IEventStore;
            var poopProjection2 = new PoopProjection();

            eventStore2.Projections.Add(poopProjection2);

            eventStore2.DehydrateProjections();

            ((PostgresEventStore)eventStore2).NumberOfInlineEventsNotCatchedUp.Should().Be(0);

            poopProjection2.PoopReport.Count.Should().Be(1);

            var snoopy2 = eventStore.Aggregates.Load<DogAggregate>(snoopyId);
            snoopy2.Poop(100);
            eventStore2.Aggregates.Store(snoopy2);
            poopProjection2.PoopReport.First().PoopTotal.Should().Be(400);

            ((PostgresEventStore)eventStore2).NumberOfInlineEventsNotCatchedUp.Should().Be(1);

            // Check that after catchup the projection is still the same and inline event list is cleaned up
            eventStore2.CatchUp();
            poopProjection2.PoopReport.First().PoopTotal.Should().Be(400);
            ((PostgresEventStore)eventStore2).NumberOfInlineEventsNotCatchedUp.Should().Be(0);

        }
    }
}
