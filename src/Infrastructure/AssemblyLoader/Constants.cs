using System.IO;

namespace Infrastructure.AssemblyLoader
{
    public static class Constants
    {
        public static string CodeBaseRootPath;
        public static string ModuleManifestFile => "module.json";
        public static string ModulesFolder => Path.Combine(Constants.CodeBaseRootPath, "modules");
        public static string HostModulesManifestFile => Path.Combine(Constants.CodeBaseRootPath, ModulesJsonFile);
        public static string ModulesJsonFile => "modules.json";
        public static string PluginsJsonFile => "plugins.json";
    }
}