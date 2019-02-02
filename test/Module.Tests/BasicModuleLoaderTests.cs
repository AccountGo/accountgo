using Xunit;
using Infrastructure.AssemblyLoader;
using Infrastructure.Module;
using Infrastructure.Extensions;
using System;
using System.Reflection;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure;

namespace Module.Tests
{
    public class BasicModuleLoaderTests
    {
        [Fact]
        public void LoadNetCoreApp21ModuleProject()
        {
            //Given
            Constants.CodeBaseRootPath = GetExecutingDirectorybyAppDomain();
            var path = Path.Combine(Constants.CodeBaseRootPath + "Modules" + "\\SampleNetStandard20\\Debug\\netstandard2.0\\", "SampleNetStandard20.dll");
            var assembly = new CustomAssemblyLoadContext().LoadFromAssemblyPath(path);
            var type = assembly.GetType("SampleNetStandard20.Class1");
            //When
            var method = type.GetMethod("GetString");
            var returnValue = method.Invoke(null, null);
            //Then
            Assert.Equal("Hello, World!", returnValue);
        }

        [Fact]
        public void GetListOfModulesInJsonFile()
        {
            //Given
            Constants.CodeBaseRootPath = GetExecutingDirectorybyAppDomain();
            AssemblyLoaderManager loader = new AssemblyLoaderManager();
            //When
            var (modules, plugins) = loader.GetAssembliesToRegister();
            //Then
            Assert.NotEmpty(modules);
        }

        [Fact]
        public void TestServiceCollection()
        {
            //Given
            Constants.CodeBaseRootPath = GetExecutingDirectorybyAppDomain();
            var services = new ServiceCollection();
            services.AddModulesToCollection(Constants.CodeBaseRootPath);
            var sp = services.BuildServiceProvider();
            var moduleInitializers = sp.GetServices<IStartup>();
            foreach (var moduleInitializer in moduleInitializers)
            {
                moduleInitializer.ConfigureServices(services);
            }
            //When
            //Then
        }

        [Fact]
        public void PrintSolutionVariables()
        {
            Console.WriteLine(GetAssemblyPathByCodeBase());
            Console.WriteLine(GetExecutingDirectorybyAppDomain());
            Console.WriteLine(GetExecutingDirectoryByAssemblyLocation());
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
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return path; // /Users/Marvs/source/accountgo/test/Module.Tests/bin/Debug/netcoreapp2.1
        }
    }
}