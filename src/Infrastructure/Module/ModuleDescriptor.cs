using System;
using System.Reflection;
using Infrastructure.AssemblyLoader;

namespace Infrastructure.Module
{
    public sealed class ModuleDescriptor : AssemblyInfo
    {
        #region Ctor
        public ModuleDescriptor(string name, Version version, Assembly assembly = null) : base (name, version, assembly)
        {
        }

        public override AssemblyInfo SetAssembly(Assembly assembly)
        {
            return new ModuleDescriptor(this.Name, this.Version, assembly);
        }
        #endregion
    }
}