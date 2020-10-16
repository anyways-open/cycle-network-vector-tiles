using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var configurationBuilder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", true, true);
                    
                // get deploy time settings if present.
                var configuration = configurationBuilder.Build();
                var deployTimeSettings = configuration["deploy-time-settings"] ?? "/var/app/config/appsettings.json";
                configurationBuilder = configurationBuilder
                    .AddJsonFile(deployTimeSettings, true, true);

                // get environment variable prefix.
                configuration = configurationBuilder.Build();
                var envVarPrefix = configuration["env-var-prefix"] ?? "CONF_";
                configurationBuilder = configurationBuilder
                    .AddEnvironmentVariables((c) => { c.Prefix = envVarPrefix; });
                
                // build configuration.
                configuration = configurationBuilder.Build();

                // hookup OsmSharp logging.
                OsmSharp.Logging.Logger.LogAction = (origin, level, message, parameters) =>
                {
                    var formattedMessage = $"{origin} - {message}";
                    switch (level)
                    {
                        case "critical":
                            Log.Fatal(formattedMessage);
                            break;
                        case "error":
                            Log.Error(formattedMessage);
                            break;
                        case "warning":
                            Log.Warning(formattedMessage);
                            break;
                        case "verbose":
                            Log.Verbose(formattedMessage);
                            break;
                        case "information":
                            Log.Information(formattedMessage);
                            break;
                        default:
                            Log.Debug(formattedMessage);
                            break;
                    }
                };

                // setup logging.
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .CreateLogger();

                try
                {
                    var source = configuration["source"];
                    var data = configuration["data"];
                    var target = configuration["target"];

                    // setup host and configure DI.
                    var host = Host.CreateDefaultBuilder(args)
                        .ConfigureServices((hostContext, services) =>
                        {
                            // add logging.
                            services.AddLogging(b =>
                                {
                                    b.AddSerilog();
                                });
                            
                            // add configuration.
                            services.AddSingleton(new WorkerConfiguration()
                                {
                                    SourceUrl = source,
                                    DataPath = data,
                                    TargetPath = target
                                });
                            
                            // add downloader.
                            services.AddSingleton<ANYWAYS.Tools.Downloader>();
                            
                            // add the service.
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