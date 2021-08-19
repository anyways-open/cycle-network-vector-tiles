using System;
using System.IO;
using System.Threading.Tasks;
using ANYWAYS.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Json;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // hardcode configuration before the configured logging can be bootstrapped.
            var logFile = Path.Combine("logs", "boot-log-.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(new JsonFormatter(), logFile, rollingInterval: RollingInterval.Day)
                .WriteTo.Console()
                .CreateLogger();
            
            try
            {
                var configurationBuilder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", true, true);

                // get deploy time setting.
                var (deployTimeSettings, envVarPrefix) = configurationBuilder.GetDeployTimeSettings();

                try
                {
                    
                    var host = Host.CreateDefaultBuilder(args)
                        .ConfigureAppConfiguration((hostingContext, config) =>
                        {
                            Log.Information("Env: {env}", hostingContext.HostingEnvironment.EnvironmentName);
                            
                            config.AddJsonFile(deployTimeSettings, true, true);
                            config.AddEnvironmentVariables((c) => { c.Prefix = envVarPrefix; });
                        })
                        .ConfigureServices((hostingContext, services) =>
                        {
                            Log.Logger = new LoggerConfiguration()
                                .ReadFrom.Configuration(hostingContext.Configuration)
                                .CreateLogger();
                            services.AddLogging(b =>
                            {
                                b.ClearProviders();
                                b.AddSerilog();
                            });
                            
                            // add configuration.
                            var source = hostingContext.Configuration["source"];
                            var data = hostingContext.Configuration["data"];
                            var target = hostingContext.Configuration["target"];
                            services.AddSingleton(new WorkerConfiguration()
                            {
                                SourceUrl = source,
                                DataPath = data,
                                TargetPath = target
                            });

                            // add downloader.
                            services.AddSingleton<Downloader>();
                            
                            // add hosted service.
                            services.AddHostedService<Worker>();
                        }).Build();
                    
                    // run!
                    await host.RunAsync();
                }
                catch (Exception e)
                {
                    Log.Logger.Fatal(e, "Unhandled exception.");
                }
            }
            catch (Exception e)
            {
                // log to console if something happens before logging gets a chance to bootstrap.
                Console.WriteLine(e);
                throw;
            }
        }
    }
}