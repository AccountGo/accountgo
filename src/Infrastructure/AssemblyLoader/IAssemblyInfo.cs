using System;
using System.Reflection;

namespace Infrastructure.AssemblyLoader
{
    public interface IAssemblyInfo
    {
        string Name { get; }
        Version Version { get; }
        Assembly Assembly { get; }
    }
}