using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace AccountGoWeb
{
    public class Program
    {
        public static void Main(string[] args) => CreateWebHostBuilder(args).Build().Run();

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseUrls("http://0.0.0.0:8000", "https://0.0.0.0:44300");
    }
}
