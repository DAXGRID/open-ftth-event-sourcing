using Microsoft.Extensions.DependencyInjection;
using OpenFTTH.EventSourcing.InMem;
using System;
using System.Reflection;

namespace OpenFTTH.EventSourcing.Tests
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IEventStore, InMemEventStore>();

            var businessAssemblies = new Assembly[] { AppDomain.CurrentDomain.Load("OpenFTTH.EventSourcing.Tests") };

            services.AddProjections(businessAssemblies);
        }
    }
}
