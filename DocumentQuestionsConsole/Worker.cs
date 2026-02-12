//using Azure.AI.Projects.OpenAI;
using DocumentQuestions.Library;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine.Parsing;
using System.Reflection;
using System.Text;
using syS = System;

namespace DocumentQuestions.Console
{
   internal class Worker : BackgroundService
   {
      private static ILogger<Worker> log;
      private static ILoggerFactory logFactory;
      private static IConfiguration config;
      private static StartArgs? startArgs;
      private static AgentUtility agentUtility;
      private static Common common;
      private static Parser rootParser;
      private static DocumentIntelligence documentIntelligence;
      private static string activeDocument = string.Empty;
      private static AiSearch aiSearch;
      private static AgentSession? currentSession = null; // Session for multi-turn conversations



      public Worker(ILogger<Worker> logger, ILoggerFactory loggerFactory, IConfiguration configuration, StartArgs sArgs, AgentUtility agentUtility, Common cmn, DocumentIntelligence documentIntel, AiSearch aiSrch)
      {
         log = logger;
         logFactory = loggerFactory;
         config = configuration;
         startArgs = sArgs;
         common = cmn;
         Worker.agentUtility = agentUtility;
         documentIntelligence = documentIntel;
         aiSearch = aiSrch;
      }

      internal static async Task AskQuestion(string[] question)
      {
         if (question == null || question.Length == 0)
         {
            return;
         }

         string quest = string.Join(" ", question);
         syS.Console.WriteLine("----------------------");

         StringBuilder responseBuilder = new();
         await foreach (var (text, session) in agentUtility.AskQuestionStreamingWithThread(quest, activeDocument, currentSession))
         {
            syS.Console.Write(text);
            responseBuilder.Append(text);
            currentSession = session; // Update session for next question
         }

         syS.Console.WriteLine("----------------------");
         syS.Console.WriteLine();
      }

      internal static Task ResetConversation()
      {
         currentSession = null;
         log.LogInformation("Conversation session reset. Starting fresh conversation.", ConsoleColor.Green);
         return Task.CompletedTask;
      }


      internal async static Task ClearIndex()
      {

         var deleted = await aiSearch.ClearIndexes([AiSearch.IndexName]);
         if (deleted.Count > 0)
         {
            log.LogInformation("The following indexes were deleted:", ConsoleColor.Yellow);
            foreach (var name in deleted)
            {
               log.LogInformation($"\t{name}");
            }
         }
         else
         {
            log.LogInformation("No indexes were deleted.", ConsoleColor.Yellow);
         }

      }

      internal async static Task<int> ListFiles(object t)
      {
         var fileNames = await aiSearch.GetDistinctFileNamesAsync();
         //var names = await aiSearch.ListAvailableIndexes();
         if (fileNames.Count > 0)
         {
            log.LogInformation("List of available documents:", ConsoleColor.Yellow);
         }
         foreach (var name in fileNames)
         {
            log.LogInformation(name);
         }
         return fileNames.Count;
      }

      internal static async Task ProcessFile(string file, string model, string index)
      {
         if (string.IsNullOrWhiteSpace(model))
         {
            model = "prebuilt-layout";
         }
         if (file.Length == 0)
         {
            log.LogInformation("Please enter a file name to process", ConsoleColor.Red);
            return;
         }
         string name = string.Join(" ", file);
         if (!File.Exists(name))
         {
            log.LogInformation($"The file {name} doesn't exist. Please enter a valid file name", ConsoleColor.Red);
            return;
         }

         await documentIntelligence.ProcessDocument(new FileInfo(name), model);

      }

      internal static void SetActiveDocument(string[] document)
      {
         var docName = string.Join(" ", document);
         activeDocument = docName;
         Worker.currentSession = null;
      }

      protected async override Task ExecuteAsync(CancellationToken stoppingToken)
      {

         Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
         rootParser = CommandBuilder.BuildCommandLine();
         string[] args = startArgs.Args;
         if (args.Length == 0) args = new string[] { "-h" };
         int val = await rootParser.InvokeAsync(args);
         bool firstPass = true;
         int fileCount = 0;
         StringBuilder sb;

         while (true)
         {
            sb = new StringBuilder();
            syS.Console.WriteLine();
            if (firstPass || string.IsNullOrWhiteSpace(activeDocument))
            {
               fileCount = await rootParser.InvokeAsync("list");
            }

            if (fileCount > 0)
            {
               if (!string.IsNullOrWhiteSpace(activeDocument))
               {
                  log.LogInformation(new() { { "Active Document: ", ConsoleColor.DarkGreen }, { activeDocument, ConsoleColor.Blue } });
               }
               else
               {
                  log.LogInformation("Please use the 'doc' command to set an active document to start asking questions. Use 'list' to show available documents or 'process' to index a new document", ConsoleColor.Yellow);
                  log.LogInformation("");
               }
            }
            else
            {
               log.LogInformation("Please use the 'process' command to process your first document.", ConsoleColor.Yellow);
               log.LogInformation("");
            }


            syS.Console.Write("dq> ");
            var line = syS.Console.ReadLine();
            if (line == null)
            {
               return;
            }
            val = await rootParser.InvokeAsync(line);
            firstPass = false;
         }
      }
   }
}
