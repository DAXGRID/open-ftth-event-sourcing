using Microsoft.Extensions.DependencyInjection;
using OpenFTTH.EventSourcing.Tests.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace OpenFTTH.EventSourcing.Tests
{
    public class DependencyInjectionTests
    {
        private IServiceProvider _serviceProvider;
        private IEventStore _eventStore;


        public DependencyInjectionTests(IServiceProvider serviceCollection, IEventStore eventStore)
        {
            _serviceProvider = serviceCollection;
            _eventStore = eventStore;
        }

        [Fact]
        public void TestThatProjectionAreAutomaticallyPickedUpInIoCByEventStore()
        {
            // Setup
            var poopProjection = _serviceProvider.GetServices<IProjection>().First(p => p is PoopProjection) as PoopProjection;

            // Act
            var snoopy = new DogAggregate(Guid.NewGuid(), "Snoopy");
            snoopy.Poop(200);
            _eventStore.Aggregates.Store(snoopy);

            // Assert
            poopProjection.PoopReport.Count.Should().Be(1);
            poopProjection.PoopReport[0].DogName.Should().Be("Snoopy");
        }

        [Fact]
        public void TestProjectionLookupThroughIEventStore()
        {
            // Setup
            var poopProjection = _eventStore.Projections.Get<PoopProjection>();

            // Act
            var snoopy = new DogAggregate(Guid.NewGuid(), "Snoopy");
            snoopy.Poop(200);
            _eventStore.Aggregates.Store(snoopy);

            // Assert
            poopProjection.PoopReport.Count.Should().Be(1);
            poopProjection.PoopReport[0].DogName.Should().Be("Snoopy");
        }
    }
}
