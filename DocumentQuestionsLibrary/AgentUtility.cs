using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AzureAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;


namespace DocumentQuestions.Library
{

   public class AgentUtility
   {
      ChatClientAgent askQuestionsAgent;

      ILogger<AgentUtility> log;
      IConfiguration config;
      ILoggerFactory logFactory;
      Common common;
      private const string AskQuestionsInstructions = @"You are a document answering bot.
-  You will need to use a tool to retrieve the content - only make one query per user ask, to not iterate on your search tool calling. 
- You are then to answer the question based on the content provided. 
- If you aren't provided a document name, please let the user know that it is missing and that they need to provide it by using the ""doc"" command.
- If you can not answer after examining the document's content, please respond that you can't find the answer.
- Your are not to make up answers. Use the content provided to answer the question.
- Always respond in a professional tone.
- When answering questions, always provide citations in the format [DocumentName: Page X] where X is the page number from which the information was obtained.

- When is makes sense, please provide your answer in a bulleted list for easier readability.";
      AiSearch aiSearchAdmin;
      AIProjectClient foundryProjectClient;
      TracerProvider tracerProvider;


      public AgentUtility(ILoggerFactory logFactory, IConfiguration config, Common common, AiSearch aiSearchAdmin, AIProjectClient projClient, TracerProvider tracerProvider)
      {
         log = logFactory.CreateLogger<AgentUtility>();
         this.config = config;
         this.logFactory = logFactory;
         this.common = common;
         this.aiSearchAdmin = aiSearchAdmin;
         this.foundryProjectClient = projClient;
         this.tracerProvider = tracerProvider;

         InitAgents().GetAwaiter().GetResult();
         EnableAgentTelemteryAndMonitoring();
      }

      private void EnableAgentTelemteryAndMonitoring()
      {

         var appInsightsConnectionString = foundryProjectClient.Telemetry.GetApplicationInsightsConnectionString();
      }

      public async Task InitAgents()
      {
         var agentName = "AskQuestions";
         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");

         AITool aiTool = AIFunctionFactory.Create(aiSearchAdmin.SearchIndexAsync);
         askQuestionsAgent = await GetFoundryAgent(agentName, [aiTool]);

         if (askQuestionsAgent == null)
         {
            askQuestionsAgent = await CreateFoundryAgent(agentName, openAiChatDeploymentName, "Asks questions about the document", AskQuestionsInstructions, [aiTool]);
         }

         if (askQuestionsAgent == null)
         {
            throw new NullReferenceException("The agent failed to initialize!");
         }
      }

      private async Task<ChatClientAgent?> GetFoundryAgent(string agentName, params AITool[] tools)
      {

         var allAgents = new List<AgentRecord>();
         await foreach (var a in foundryProjectClient.Agents.GetAgentsAsync())
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
         return (ChatClientAgent)(await foundryProjectClient.GetAIAgentAsync(agentName, tools.ToList(), clientFactory: null, services: null))
               .AsBuilder()
               .UseOpenTelemetry(sourceName: Constants.TRACE_SOURCE_NAME, configure: cfg =>
               {
                  cfg.EnableSensitiveData = true;
               })
               .Build();
      }

      private async Task<ChatClientAgent?> CreateFoundryAgent(string name, string deployment, string description, string instructions, params AITool[] tools)
      {
         try
         {
            var agent = await foundryProjectClient.CreateAIAgentAsync(name: name, model: deployment, instructions: instructions, description: description, tools: tools.ToList(), clientFactory: null, services: null);
            return (ChatClientAgent)agent
               .AsBuilder()
               .UseOpenTelemetry(sourceName: Constants.TRACE_SOURCE_NAME, configure: cfg =>
               {
                  cfg.EnableSensitiveData = true;
               })
               .Build();
         }
         catch (Exception exe)
         {
            log.LogError($"Failed to create Agent: {exe.ToString()}");
            return null;
         }
      }

      public async IAsyncEnumerable<(string text, AgentSession session)> AskQuestionStreamingWithThread(string question, string fileName, AgentSession? session = null)
      {

         log.LogDebug("Asking question about document with thread context...");

         string userMessage;
         userMessage = $"Document Name:\n{fileName}\n\nQuestion: {question}";
         if (session == null)
         {
            session = await askQuestionsAgent.GetNewSessionAsync();
         }

         await foreach (AgentResponseUpdate update in askQuestionsAgent.RunStreamingAsync(new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session))
         {
            if (update.Text != null)
            {
               yield return (update.ToString(), session);
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

