using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure
{
    /// <summary>
    /// Abstraction to use by modules and plugins.
    /// </summary>
    public interface IStartup
    {
        void ConfigureServices(IServiceCollection services, IConfiguration configuration = null);
        void Configure(IApplicationBuilder application);
    }
}