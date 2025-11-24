using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using DocumentQuestions.Library;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;


namespace DocumentQuestions.Console
{
   internal class Program
   {
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

         var prjEndpoint = config.GetValue<Uri>(Constants.AIFOUNDRY_PROJECT_ENDPOINT) ?? throw new ArgumentException($"Missing {Constants.AIFOUNDRY_PROJECT_ENDPOINT} in configuration");
         var projectClient = new AIProjectClient(prjEndpoint, new DefaultAzureCredential());
         var appInsightsConnectionString = projectClient.Telemetry.GetApplicationInsightsConnectionString();
         var otelResourceBuilder = ResourceBuilder.CreateDefault().AddService("DocumentQuestions.Console");

         var builder = new HostBuilder()
             .ConfigureLogging(logging =>
             {
                logging.SetMinimumLevel(level);
                logging.AddFilter("System", LogLevel.Warning);
                logging.AddFilter("Microsoft", LogLevel.Warning);
                logging.AddConsoleFormatter<CustomConsoleFormatter, ConsoleFormatterOptions>();
                logging.AddConsole(options =>
                {
                   options.FormatterName = "custom";

                });
                //add open telemetry logging
                logging.AddOpenTelemetry(options =>
                {
                   options.SetResourceBuilder(otelResourceBuilder);
                   options.IncludeScopes = true;
                   options.IncludeFormattedMessage = true;
                   options.AddAzureMonitorLogExporter(monitor =>
                   {
                      monitor.ConnectionString = appInsightsConnectionString;
                   });
                });
             })

            .ConfigureAppConfiguration((hostContext, appConfiguration) =>
            {
               appConfiguration.SetBasePath(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
               appConfiguration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
               appConfiguration.AddJsonFile("local.settings.json", optional: false, reloadOnChange: true);
               appConfiguration.AddEnvironmentVariables();
            })

            .ConfigureServices((hostContext, services) =>
            {
               services.AddSingleton<StartArgs>(new StartArgs(args));
               services.AddSingleton<AgentUtility>();
               services.AddSingleton<DocumentIntelligence>();
               services.AddSingleton<AiSearch>();
               services.AddSingleton<AIProjectClient>(projectClient);
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
               //add open telemetry logging for the agents
               services.AddOpenTelemetry().WithTracing(builder =>
               {
                  builder.SetResourceBuilder(otelResourceBuilder)
                     .AddSource(Constants.TRACE_SOURCE_NAME)
                     .AddSource("*Microsoft.Extensions.AI") 
                     .AddSource("*Microsoft.Extensions.Agents*") 
                     .AddAzureMonitorTraceExporter(options =>
                     {
                        options.ConnectionString = appInsightsConnectionString;
                     });
               }).WithMetrics(builder =>
               {
                  builder.SetResourceBuilder(otelResourceBuilder)
                     .AddMeter("DocumentQuestionsAgentDemo")
                     .AddMeter("Microsoft.Agents.AI*")
                     .AddAzureMonitorMetricExporter(options =>
                     {
                        options.ConnectionString = appInsightsConnectionString;
                     });
               }).WithLogging();

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
