using Xunit;
using Infrastructure.AssemblyLoader;
using System;
using System.Reflection;
using System.IO;

namespace Module.Tests
{
    public class BasicModuleLoaderTests
    {
        /// TODO: add SampleModulesRefs.targets so that when Module.Tests projects builds, sample modules copied to test project Modules folder
        private const string assemblyPath = "/Users/Marvs/source/accountgo/test/Module.Tests/Modules/";
        [Fact]
        public void LoadNetCoreApp21ModuleProject()
        {
            //Given
            string path = assemblyPath + "SampleNetStandard20/netstandard2.0/SampleNetStandard20.dll";
            var assembly = new CustomAssemblyLoadContext().LoadFromAssemblyPath(path);
            var type = assembly.GetType("SampleNetStandard20.Class1");
            //When
            var method = type.GetMethod("GetString");
            var returnValue = method.Invoke(null, null);
            //Then
            Assert.Equal("Hello, World!", returnValue);
        }

        private string GetAssemblyPathByCodeBase()
        {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            return Path.GetDirectoryName(Uri.UnescapeDataString(uri.Path)); // /Users/Marvs/source/accountgo/test/Module.Tests/bin/Debug/netcoreapp2.1
        }

        private string GetExecutingDirectorybyAppDomain()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory;
            return path; // /Users/Marvs/source/accountgo/test/Module.Tests/bin/Debug/netcoreapp2.1/
        }

        private string GetExecutingDirectoryByAssemblyLocation()
        {
            string path= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return path; // /Users/Marvs/source/accountgo/test/Module.Tests/bin/Debug/netcoreapp2.1
        }
    }
}