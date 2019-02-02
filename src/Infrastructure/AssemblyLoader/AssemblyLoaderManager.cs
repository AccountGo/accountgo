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
        public (IEnumerable<ModuleDescriptor>, IEnumerable<PluginDescriptor>) GetAssembliesToRegister()
        {
            List<ModuleDescriptor> modulesAssemblies = new List<ModuleDescriptor>();
            List<PluginDescriptor> pluginAssemblies = new List<PluginDescriptor>();

            try
            {
                using (var reader = new StreamReader(Constants.HostModulesManifestFile))
                {
                    dynamic modules = JsonConvert.DeserializeObject(reader.ReadToEnd());
                    foreach (dynamic module in modules)
                    {
                        Console.WriteLine("name: {0}, version: {1}", module.name, Version.Parse(module.version.ToString()));
                        string[] modFolder = Directory.GetDirectories(Constants.ModulesFolder, module.name.ToString());
                        if(modFolder.Length == 0 || modFolder == null)
                            throw new Exception("Module " + module.name.ToString() + " not found.");
                        var mod = new DirectoryInfo(modFolder[0]); //TODO: Must improve this. Locate the the module recursively under modules folder
                        var assembly = System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(mod.Name));
                        modulesAssemblies.Add(
                            new ModuleDescriptor(
                                module.name.ToString(),
                                Version.Parse(module.version.ToString()),
                                assembly));
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
    }
}