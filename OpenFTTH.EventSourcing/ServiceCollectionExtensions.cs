using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenFTTH.EventSourcing
{ 
    public static class ServiceCollectionExtensions
    {
        public static void AddProjections(this IServiceCollection services, IEnumerable<Assembly> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                RegisterHandlersOfType(services, assembly, typeof(IProjection));
            }
        }

        private static void RegisterHandlersOfType(this IServiceCollection services, Assembly assembly, Type @interface)
        {
            var handlers = assembly.GetTypes().Where(t => @interface.IsAssignableFrom(t));

            foreach (var handler in handlers)
            {
                services.AddSingleton(typeof(IProjection), handler);
            }
        }
    }
}
        
