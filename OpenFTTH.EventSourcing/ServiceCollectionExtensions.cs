using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenFTTH.EventSourcing
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddProjections(
            this IServiceCollection services,
            IEnumerable<Assembly> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                RegisterHandlersOfType(services, assembly, typeof(IProjection));
            }

            return services;
        }

        private static IServiceCollection RegisterHandlersOfType(
            this IServiceCollection services,
            Assembly assembly, Type @interface)
        {
            var handlers = assembly.GetTypes().Where(t => @interface.IsAssignableFrom(t));

            foreach (var handler in handlers)
            {
                services.AddSingleton(typeof(IProjection), handler);
            }

            return services;
        }
    }
}

