using System;
using System.IO;
using Infrastructure.AssemblyLoader;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AccountGoWeb
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; }

        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();

            string urlhost = System.Environment.GetEnvironmentVariable("APIHOST") ?? "localhost";
            Configuration["ApiUrl"] = $"http://{urlhost}:8001/api/";
            System.Console.WriteLine("API full url is http://" + urlhost + ":8001/api/");
            System.Console.WriteLine("ApiUrl = " + Configuration["ApiUrl"]);
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var mvcBuilder = services
            .AddMvc()
            .SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Version_2_1);

            services.AddSingleton<IConfiguration>(Configuration);

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o => o.LoginPath = new PathString("/account/signin"));

            // Load and install modules. Get all module folder names inside 'Modules' folder
            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "modules")))
            {
                foreach (var dir in Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "modules")))
                {
                    var moduleAssembly = new CustomAssemblyLoadContext().LoadFromAssemblyPath(Path.Combine(dir, Path.GetFileName(dir)) + ".dll");
                    Console.WriteLine($"Loading application parts from module {moduleAssembly.FullName}");

                    // This loads MVC application parts from module assemblies
                    var partFactory = ApplicationPartFactory.GetApplicationPartFactory(moduleAssembly);
                    foreach (var part in partFactory.GetApplicationParts(moduleAssembly))
                    {
                        Console.WriteLine($"* {part.Name}");
                        mvcBuilder.PartManager.ApplicationParts.Add(part);
                    }

                    // This piece finds and loads related parts, such as SampleModule.Views.dll.
                    var relatedAssemblies = RelatedAssemblyAttribute.GetRelatedAssemblies(moduleAssembly, throwOnError: true);
                    foreach (var assembly in relatedAssemblies)
                    {
                        partFactory = ApplicationPartFactory.GetApplicationPartFactory(assembly);
                        foreach (var part in partFactory.GetApplicationParts(assembly))
                        {
                            Console.WriteLine($"  * {part.Name}");
                            mvcBuilder.PartManager.ApplicationParts.Add(part);
                        }
                    }
                }
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseStaticFiles();

            app.UseAuthentication();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
