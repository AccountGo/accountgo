using System;
using System.Reflection;
using Infrastructure.AssemblyLoader;

namespace Infrastructure.Plugin
{
    public sealed class PluginDescriptor : AssemblyInfo
    {
        #region Ctor
        public PluginDescriptor(string name, Version version, Assembly assembly = null) : base (name, version, assembly)
        {
        }

        public override AssemblyInfo SetAssembly(Assembly assembly)
        {
            return new PluginDescriptor(this.Name, this.Version, assembly);
        }
        #endregion
    }
}