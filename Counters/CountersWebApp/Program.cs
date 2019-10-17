using Counters.Request;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace CountersWebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    RequestCountersEventSource.Initialize();

                    webBuilder.UseStartup<Startup>();
                });
    }
}
