using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFTTH.EventSourcing.Tests.Model;
using System;
using System.Linq;
using Xunit;

namespace OpenFTTH.EventSourcing.Tests
{
    public class DependencyInjectionTests
    {
        private IServiceProvider _serviceProvider;
        private IEventStore _eventStore;

        public DependencyInjectionTests(
            IServiceProvider serviceProvider,
            IEventStore eventStore)
        {
            _serviceProvider = serviceProvider;
            _eventStore = eventStore;
        }

        [Fact]
        public void TestRegistrationOfProjections()
        {
            _eventStore.ScanForProjections();

            // Setup
            var poopProjection = _serviceProvider
                .GetServices<IProjection>()
                .First(p => p is PoopProjection) as PoopProjection;

            // Act
            var snoopy = new DogAggregate(Guid.NewGuid(), "Snoopy");
            snoopy.Poop(200);
            _eventStore.Aggregates.Store(snoopy);

            // Assert
            poopProjection.PoopReport
                .Find(d => d.DogName == "Snoopy")
                .PoopTotal
                .Should().Be(200);
        }

        [Fact]
        public void TestProjectionLookupThroughIEventStore()
        {
            // Setup
            var poopProjection = _eventStore.Projections.Get<PoopProjection>();

            // Act
            var snoopy = new DogAggregate(Guid.NewGuid(), "Pluto");
            snoopy.Poop(500);
            _eventStore.Aggregates.Store(snoopy);

            // Assert
            poopProjection.PoopReport.Find(d => d.DogName == "Pluto").PoopTotal.Should().Be(500);
        }
    }
}
