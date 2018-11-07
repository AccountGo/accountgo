using System.Linq;
using Infrastructure.AssemblyLoader;
using Infrastructure.Module;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        private static readonly AssemblyLoaderManager loader = new AssemblyLoaderManager();
        public static IServiceCollection AddModulesToCollection(this IServiceCollection services, string modulesRootPath)
        {
            var (modules, _) = loader.GetAssembliesToRegister();
            foreach (var module in modules)
            {
                RegisterAssemblyToServiceColetion(module, ref services);
            }
            return services;
        }

        private static void RegisterAssemblyToServiceColetion(ModuleDescriptor module, ref IServiceCollection services)
        {
            var moduleInitializerType = module.Assembly.GetTypes().FirstOrDefault(t => typeof(IStartup).IsAssignableFrom(t));
            if ((moduleInitializerType != null) && (moduleInitializerType != typeof(IStartup)))
            {
                services.AddSingleton(typeof(IStartup), moduleInitializerType);
            }
        }
    }
}