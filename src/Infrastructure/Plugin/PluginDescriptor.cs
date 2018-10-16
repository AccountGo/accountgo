using System;
using Infrastructure.AssemblyLoader;

namespace Infrastructure.Plugin
{
    public sealed class PluginDescriptor : AssemblyInfo
    {
        #region Ctor
        public PluginDescriptor(string name, Version version) : base(name, version)
        {
        }
        #endregion
    }
}