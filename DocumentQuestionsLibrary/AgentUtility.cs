using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

//using OpenAI;
//using OpenAI.Assistants;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions; // Added for sanitizing tool names

namespace DocumentQuestions.Library
{

   public class AgentUtility
   {
      AIAgent askQuestionsAgent;

      ILogger<AgentUtility> log;
      IConfiguration config;
      ILoggerFactory logFactory;
      Common common;
      LocalToolsUtility localToolsUtility;

      // Prompt templates as constants (converted from YAML)
      private const string AskQuestionsInstructions = @"You are a document answering bot.
-  You will need to use a tool to retrieve the content - only make one query per user ask, to not iterate on your search tool calling. 
- You are then to answer the question based on the content provided. 
- If you aren't provided a document name, please let the user know that it is missing and that they need to provide it by using the ""doc"" command.
- If you can not answer after examining the document's content, please respond that you can't find the answer.
- Your are not to make up answers. Use the content provided to answer the question.
- Always respond in a professional tone.
- When answering questions, always provide citations in the format [DocumentName: Page X] where X is the page number from which the information was obtained.

- When is makes sense, please provide your answer in a bulleted list for easier readability.";
      AgentThread askQuestionsAgentThread;
      AiSearch aiSearchAdmin;
      AIProjectClient foundryProject;
      PersistentAgentsClient foundryAgentsClient;

      public AgentUtility(ILoggerFactory logFactory, IConfiguration config, Common common, AiSearch aiSearchAdmin, LocalToolsUtility localToolsUtility)
      {
         log = logFactory.CreateLogger<AgentUtility>();
         this.config = config;
         this.logFactory = logFactory;
         this.common = common;
         this.aiSearchAdmin = aiSearchAdmin;

         this.foundryProject = new AIProjectClient(new Uri(config["AIFOUNDRY_ENDPOINT"]), new DefaultAzureCredential());
         this.foundryAgentsClient = foundryProject.GetPersistentAgentsClient();

         this.localToolsUtility = localToolsUtility;

         InitAgents().GetAwaiter().GetResult();
      }

      public async Task InitAgents()
      {
         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");

         askQuestionsAgent = await GetFoundryAgent("AskQuestions");
         if (askQuestionsAgent != null)
         {
            localToolsUtility.RegisterLocalToolMethods(aiSearchAdmin);
         }
         if (askQuestionsAgent == null)
         {
            var tool = localToolsUtility.CreateToolDefinitionFromMethod(aiSearchAdmin.SearchIndexAsync);
            askQuestionsAgent = await CreateFoundryAgent("AskQuestions", openAiChatDeploymentName, "Asks questions about the document", AskQuestionsInstructions, [tool]);

         }
      }

      private async Task<AIAgent> GetFoundryAgent(string agentName)
      {
         var allAgents = new List<PersistentAgent>(); // Use concrete element type if known instead of object
         await foreach (var a in foundryAgentsClient.Administration.GetAgentsAsync())
         {
            allAgents.Add(a);
         }

         // Filter by name
         var named = allAgents
            .Where(a => a.Name == agentName)
            .ToList();

         if (named.Count > 1)
         {
            throw new InvalidOperationException($"Expected one agent with name '{agentName}', but found {named.Count}.");
         }

         if (named.Count == 0)
         {
            return null;
         }
         // Extract Id property reflectively (replace with strongly typed access if available)
         var id = named[0].Id.ToString()
                  ?? throw new InvalidOperationException("Matched agent is missing Id.");

         return foundryAgentsClient.GetAIAgent(id, new ChatClientAgentOptions()
         {

            //AIContextProviderFactory = ctx => new UserInfoMemory(chatClient.AsIChatClient(), ctx.SerializedState, ctx.JsonSerializerOptions)
         });
      }

      private async Task<AIAgent> CreateFoundryAgent(string name, string deployment, string description, string instructions, params ToolDefinition[]  tools)
      {

         try
         {
            var agent = await foundryAgentsClient.CreateAIAgentAsync(
                name: name,
                model: deployment,
                description: description,
                instructions: instructions,

                tools: tools);

            return agent;
         }
         catch(Exception exe)
         {
            log.LogError($"Failed to create Agent: {exe.ToString()}");
            return null;
         }
      }

      public async IAsyncEnumerable<(string text, PersistentAgentThread thread)> AskQuestionStreamingWithThread(string question, string fileName, PersistentAgentThread thread = null)
      {
         log.LogDebug("Asking question about document with thread context...");

         string userMessage;
         userMessage = $"Document Name:\n{fileName}\n\nQuestion: {question}";


         StringBuilder assistantBuilder = new();
         await foreach (var update in askQuestionsAgent.RunStreamingAsyncWithLocalTools(localToolsUtility,foundryAgentsClient, userMessage, thread))
         {
            if (update.text != null)
            {
               assistantBuilder.Append(update.text);
               yield return (update.text, update.thread);
            }
         }

      }


   }
   public sealed record SemanticMemoryResult(string? Id, string? FileName, string? Content, double? Score);

   public enum AgentStatus
   {
      New,
      Preexisting
   }
}

