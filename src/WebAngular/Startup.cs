using Microsoft.AspNet.Authentication.Cookies;
using Microsoft.AspNet.Authentication.OpenIdConnect;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens;
using System.Threading.Tasks;

namespace WebAngular
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json");

            Configuration = builder.Build();
        }
        public IConfigurationRoot Configuration { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            // Add the platform handler to the request pipeline.
            app.UseIISPlatformHandler();

            app.UseDefaultFiles();

            app.UseStaticFiles();

            app.UseCookieAuthentication(options =>
            {
                options.AutomaticAuthenticate = true;
            });

            app.UseOpenIdConnectAuthentication(options =>
            {
                options.AutomaticChallenge = true;
                options.ClientId = Configuration["Authentication:AzureAd:ClientId"];
                options.Authority = Configuration["Authentication:AzureAd:AADInstance"] + "Common";
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Instead of using the default validation (validating against a single issuer value, as we do in line of business apps),
                    // we inject our own multitenant validation logic
                    ValidateIssuer = false,

                    // If the app needs access to the entire organization, then add the logic
                    // of validating the Issuer here.
                    // IssuerValidator
                };
                options.Events = new OpenIdConnectEvents()
                {
                    OnAuthenticationValidated = (context) =>
                    {
                        // If your authentication logic is based on users then add your logic here
                        return Task.FromResult(0);
                    },
                    OnAuthenticationFailed = (context) =>
                    {
                        context.Response.Redirect("/Home/Error");
                        context.HandleResponse(); // Suppress the exception
                        return Task.FromResult(0);
                    }
                };
            });

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    
        // Entry point for the application.
        public static void Main(string[] args) => WebApplication.Run<Startup>(args);
    }
}
