using System;
using Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


namespace SampleNetStandard20
{
    public class Class1 : IStartup
    {
        public static string GetString() => "Hello, World!";

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration = null)
        {
        }

        public void Configure(IApplicationBuilder application)
        {
        }
    }
}
