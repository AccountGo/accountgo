using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.Data.Entity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.PlatformAbstractions;

namespace Api
{
    public class Startup
    {
        private readonly IApplicationEnvironment _appEnv;
        private readonly IHostingEnvironment _hostingEnv;

        public Startup(IHostingEnvironment hostingEnv, IApplicationEnvironment appEnv)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables();
            Configuration = builder.Build();

            _hostingEnv = hostingEnv;
            _appEnv = appEnv;
        }

        public IConfigurationRoot Configuration { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            string connectionString = "";

            if (_hostingEnv.IsDevelopment())
                connectionString = Configuration["Data:LocalConnection:ConnectionString"];
            else
                connectionString = Configuration["Data:DefaultConnection:ConnectionString"];

            services.AddEntityFramework()
                .AddSqlServer()
                .AddDbContext<Data.ApiDbContext>(options => options.UseSqlServer(connectionString));

            // Add framework services.
            services.AddMvc();

            services.AddSwaggerGen();

            // Add cors
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder =>
                    {
                        builder
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                    });
            });

            // generic repository
            services.AddScoped(typeof(Core.Data.IRepository<>), typeof(Data.EfRepository<>));
            
            // domain services
            services.AddScoped(typeof(Services.Sales.ISalesService), typeof(Services.Sales.SalesService));
            services.AddScoped(typeof(Services.Financial.IFinancialService), typeof(Services.Financial.FinancialService));
            services.AddScoped(typeof(Services.Inventory.IInventoryService), typeof(Services.Inventory.InventoryService));
            services.AddScoped(typeof(Services.Purchasing.IPurchasingService), typeof(Services.Purchasing.PurchasingService));
            services.AddScoped(typeof(Services.Administration.IAdministrationService), typeof(Services.Administration.AdministrationService));
            services.AddScoped(typeof(Services.Security.ISecurityService), typeof(Services.Security.SecurityService));
            services.AddScoped(typeof(Services.TaxSystem.ITaxService), typeof(Services.TaxSystem.TaxService));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseIISPlatformHandler();

            app.UseStaticFiles();

            app.UseMvc();

            app.UseSwaggerGen();
            app.UseSwaggerUi();

            app.UseCors("AllowAllOrigins");
        }

        // Entry point for the application.
        public static void Main(string[] args) => WebApplication.Run<Startup>(args);
    }
}
