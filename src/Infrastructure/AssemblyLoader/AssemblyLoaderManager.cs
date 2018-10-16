using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Infrastructure.Module;
using Infrastructure.Plugin;
using Newtonsoft.Json;

namespace Infrastructure.AssemblyLoader
{
    public class AssemblyLoaderManager
    {
        public (IEnumerable<ModuleDescriptor>, IEnumerable<PluginDescriptor>) GetAssembliesToLoad()
        {
            List<ModuleDescriptor> modulesAssemblies = new List<ModuleDescriptor>();
            List<PluginDescriptor> pluginAssemblies = new List<PluginDescriptor>();

            try
            {
                var modulesPath = Path.Combine(Constants.CodeBaseRootPath, ModulesJsonFile);
                using (var reader = new StreamReader(modulesPath))
                {
                    dynamic modules = JsonConvert.DeserializeObject(reader.ReadToEnd());
                    foreach (dynamic module in modules)
                    {
                        Console.WriteLine("name: {0}, version: {1}", module.name, Version.Parse(module.version.ToString()));
                        modulesAssemblies.Add(new ModuleDescriptor(module.name.ToString(), Version.Parse(module.version.ToString())));
                    }
                }
            }
            catch (Exception ex)
            {
                 // TODO: Handle the log errors
                Console.WriteLine(ex.Message);
            }

            return (modulesAssemblies, pluginAssemblies);
        }
        private static readonly string ModulesJsonFile= "modules.json";
        private static readonly string PluginsJsonFile = "plugins.json";
    }
}