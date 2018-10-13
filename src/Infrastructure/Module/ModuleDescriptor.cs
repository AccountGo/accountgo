using System;
using System.Reflection;

namespace Infrastructure.Module
{
    public sealed class ModuleDescriptor
    {
        public ModuleDescriptor(string id, string name, Version version, Assembly assembly)
        {
            this.Id = id;
            this.Name = name;
            this.Version = version;
            this.Assembly = assembly;

        }
        public readonly string Id;
        public readonly string Name;
        public readonly Version Version;
        public readonly Assembly Assembly;
    }
}