using System;
using Infrastructure.AssemblyLoader;

namespace Infrastructure.Module
{
    public sealed class ModuleDescriptor : AssemblyInfo
    {
        #region Ctor
        public ModuleDescriptor(string name, Version version) : base (name, version)
        {
        }
        #endregion
    }
}