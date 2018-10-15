
using System.Reflection;
using System.Runtime.Loader;

namespace Infrastructure.AssemblyLoader
{
    public class CustomAssemblyLoadContext : AssemblyLoadContext
    {
        protected override Assembly Load(AssemblyName assemblyName)
        {
            return Assembly.Load(assemblyName);
        }
        public static (CustomAssemblyLoadContext, Assembly) CreateContext(string assemblyPath)
        {
            var context = new CustomAssemblyLoadContext();
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            return (context, assembly);
        }

        public static (CustomAssemblyLoadContext, Assembly, T) CreateContext<T>(string assemblyPath, string typeName)
        {
            var (customAssemblyLoadContext, assembly) = CreateContext(assemblyPath);
            T obj = (T)assembly.CreateInstance(typeName);
            return (customAssemblyLoadContext, assembly, obj);
        }
    }
}