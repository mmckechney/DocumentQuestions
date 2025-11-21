using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using DocumentQuestions.Library;
using Azure.AI.DocumentIntelligence;
using Azure;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Microsoft.Extensions.Options;

namespace DocumentQuestions.Console
{
   internal class Program
   {
      //private static ILogger log;
      //private static ILoggerFactory logFactory;

      public static void Main(string[] args)
      {
         CreateHostBuilder(args).Build().Run();
      }

      public static IHostBuilder CreateHostBuilder(string[] args)
      {
         //Get log level args at startup if provided...
         (LogLevel level, bool set) = GetLogLevel(args);
         if (set)
         {
            System.Console.WriteLine($"Log level set to '{level.ToString()}'");
            args = new string[] { "--help" };
         }

         // Build the configuration
         var config = new ConfigurationBuilder()
             .SetBasePath(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location))
             .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
             .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
             .AddEnvironmentVariables()
             .Build();


         var connectionString = config.GetValue<string>(Constants.APPLICATIONINSIGHTS_CONNECTION_STRING);//?? throw new ArgumentException($"Missing {Constants.APPINSIGHTS_CONNECTION_STRING} in configuration");
         ResourceBuilder resourceBuilder = null;
         if (!string.IsNullOrWhiteSpace(connectionString))
         {
            resourceBuilder = ResourceBuilder
                .CreateDefault()
                .AddService("DocumentQuestions.Console");

            // Enable model diagnostics with sensitive data.
            AppContext.SetSwitch("Microsoft.Agents.AI.EnableOTelDiagnosticsSensitive", true);

            using var traceProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddSource("Microsoft.Agents.AI*")
                .AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString)
                .Build();

            using var meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddMeter("Microsoft.Agents.AI*")
                .AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString)
                .Build();
         }

         var builder = new HostBuilder()
             .ConfigureLogging(logging =>
             {
                logging.SetMinimumLevel(level);
                logging.AddFilter("System", LogLevel.Warning);
                logging.AddFilter("Microsoft", LogLevel.Warning);
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                   logging.AddOpenTelemetry(options =>
                   {
                      options.SetResourceBuilder(resourceBuilder);
                      options.AddAzureMonitorLogExporter(options => options.ConnectionString = connectionString);
                      // Format log messages. This is default to false.
                      options.IncludeFormattedMessage = true;
                      options.IncludeScopes = true;
                   });
                }

                logging.AddConsoleFormatter<CustomConsoleFormatter, ConsoleFormatterOptions>();
                logging.AddConsole(options =>
                {
                   options.FormatterName = "custom";

                });
             })
            .ConfigureServices((hostContext, services) =>
            {
               services.AddSingleton<StartArgs>(new StartArgs(args));
               services.AddSingleton<AgentUtility>();
               services.AddSingleton<DocumentIntelligence>();
               services.AddSingleton<AiSearch>();
               services.AddSingleton(sp =>
               {
                  var config = sp.GetRequiredService<IConfiguration>();

                  var endpoint = config.GetValue<Uri>(Constants.DOCUMENTINTELLIGENCE_ENDPOINT) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_ENDPOINT} in configuration");
                  var key = config.GetValue<string>(Constants.DOCUMENTINTELLIGENCE_KEY) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_KEY} in configuration");
                  return new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(key));
               });
               services.AddSingleton<Common>();
               services.AddHostedService<Worker>();
               services.AddSingleton<ConsoleFormatter, CustomConsoleFormatter>();
            })
            
             
             .ConfigureAppConfiguration((hostContext, appConfiguration) =>
             {
                appConfiguration.SetBasePath(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
                appConfiguration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                appConfiguration.AddJsonFile("local.settings.json", optional: false, reloadOnChange: true);
                appConfiguration.AddEnvironmentVariables();
             });
         return builder;
      }

      private static (LogLevel, bool) GetLogLevel(string[] args)
      {
         if (args.Contains("--debug"))
         {
            return (LogLevel.Debug, true);
         }
         else if (args.Contains("--trace"))
         {
            return (LogLevel.Trace, true);
         }
         else if (args.Contains("--info"))
         {
            return (LogLevel.Information, true);
         }
         else if (args.Contains("--warn"))
         {
            return (LogLevel.Warning, true);
         }
         else if (args.Contains("--error"))
         {
            return (LogLevel.Error, true);
         }
         else if (args.Contains("--critical"))
         {
            return (LogLevel.Critical, true);
         }
         else if (args.Contains("--default"))
         {
            return (LogLevel.Information, true);
         }
         else
         {
            return (LogLevel.Information, false);
         }
      }
   }
}
