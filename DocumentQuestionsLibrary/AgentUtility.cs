using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI.AzureAI;

namespace DocumentQuestions.Library
{

   public class AgentUtility
   {
      AIAgent askQuestionsAgent;

      ILogger<AgentUtility> log;
      IConfiguration config;
      ILoggerFactory logFactory;
      Common common;


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


      public AgentUtility(ILoggerFactory logFactory, IConfiguration config, Common common, AiSearch aiSearchAdmin)
      {
         log = logFactory.CreateLogger<AgentUtility>();
         this.config = config;
         this.logFactory = logFactory;
         this.common = common;
         this.aiSearchAdmin = aiSearchAdmin;

         this.foundryProject = new AIProjectClient(new Uri(config["AIFOUNDRY_ENDPOINT"]), new DefaultAzureCredential());
         
         InitAgents().GetAwaiter().GetResult();
      }

      public async Task InitAgents()
      {
         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");
         
         AITool aiTool = AIFunctionFactory.Create(aiSearchAdmin.SearchIndexAsync);
         askQuestionsAgent = await GetFoundryAgent("AskQuestions", [aiTool]);

         if (askQuestionsAgent == null)
         {
            askQuestionsAgent = await CreateFoundryAgent("AskQuestions", openAiChatDeploymentName, "Asks questions about the document", AskQuestionsInstructions, [aiTool]);
         }
      }

      private async Task<AIAgent> GetFoundryAgent(string agentName, params AITool[] tools)
      {
    
         var allAgents = new List<AgentRecord>();
         await foreach (var a in foundryProject.Agents.GetAgentsAsync())
         {
            allAgents.Add(a);
         }

         // Filter by name
         var named = allAgents
            .Where(a => a.Name == agentName)
            .ToList();

         if (named.Count == 0)
         {
            return null;
         }
        
         //Need to add local tools each time you "get" the an existing agent
         return foundryProject.GetAIAgent(agentName, tools);
      }

      private async Task<AIAgent> CreateFoundryAgent(string name, string deployment, string description, string instructions, params AITool[]  tools)
      {
         try
         {
            var agent = await foundryProject.CreateAIAgentAsync(name: name, description: description, instructions: instructions, tools: tools, model: deployment);
            return agent;
         }
         catch (Exception exe)
         {
            log.LogError($"Failed to create Agent: {exe.ToString()}");
            return null;
         }
      }

      public async IAsyncEnumerable<(string text, AgentThread thread)> AskQuestionStreamingWithThread(string question, string fileName, AgentThread thread = null)
      {
         log.LogDebug("Asking question about document with thread context...");

         string userMessage;
         userMessage = $"Document Name:\n{fileName}\n\nQuestion: {question}";
         if (thread == null)
         {
            thread = askQuestionsAgent.GetNewThread();
         }

         await foreach (AgentRunResponseUpdate update in askQuestionsAgent.RunStreamingAsync(new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, thread))
         {
            if (update.Text != null)
            {
               yield return (update.ToString(), thread);
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

