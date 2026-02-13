using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AzureAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using System.ComponentModel;


namespace DocumentQuestions.Library
{

   public class AgentUtility
   {
      AIAgent askQuestionsAgent;
      AIAgent summarizerAgent;
      AIAgent crossDocumentAgent;
      AIAgent routerAgent;

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

      private const string SummarizerInstructions = @"You are a document summarization bot.
- You will use a tool to retrieve the content of a document.
- You are to provide a clear, concise summary of the document's content.
- Structure your summary with:
  - A brief overview (2-3 sentences)
  - Key points as a bulleted list
  - Any notable details or conclusions
- Always provide citations in the format [DocumentName: Page X] where X is the page number.
- Do not make up information. Only summarize what is in the document.
- Always respond in a professional tone.";

      private const string CrossDocumentInstructions = @"You are a cross-document answering bot.
- You will use a tool to search across ALL available documents to find relevant information.
- You are to answer the question based on the content found across multiple documents.
- When answering, always cite which document the information came from using the format [DocumentName: Page X].
- If information is found in multiple documents, synthesize the answer and cite all relevant sources.
- If you cannot find the answer in any document, please respond that you can't find the answer.
- Do not make up answers. Use the content provided to answer the question.
- Always respond in a professional tone.
- When it makes sense, please provide your answer in a bulleted list for easier readability.";

      private const string RouterInstructions = @"You are an intelligent routing agent for a document question-answering system.
Your job is to analyze the user's request and delegate it to the appropriate specialist agent using the tools available to you.

You have three tools available:
1. ask_single_document - Use when the user asks a question about a specific document and an active document is set.
2. ask_cross_document - Use when the user wants to search across all documents, or when no specific document is mentioned.
3. summarize_document - Use when the user asks for a summary or overview of a document.

Rules:
- If the user says ""summarize"", ""summary"", or ""overview"", use the summarize_document tool.
- If the user mentions ""all documents"", ""across documents"", ""every document"", or no specific document context is provided, use the ask_cross_document tool.
- For all other questions when an active document is set, use the ask_single_document tool.
- Simply pass through the response from the specialist agent. Do not add your own commentary.
- Only call one tool per user request.";

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
         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");

         // Initialize AskQuestions agent (single-document)
         var askAgentName = "AskQuestions";
         AITool askTool = AIFunctionFactory.Create(aiSearchAdmin.SearchIndexAsync);
         askQuestionsAgent = await GetFoundryAgent(askAgentName, [askTool]);
         if (askQuestionsAgent == null)
         {
            askQuestionsAgent = await CreateFoundryAgent(askAgentName, openAiChatDeploymentName, "Asks questions about the document", AskQuestionsInstructions, [askTool]);
         }
         if (askQuestionsAgent == null)
         {
            throw new NullReferenceException("The AskQuestions agent failed to initialize!");
         }

         // Initialize Summarizer agent
         var summarizerName = "Summarizer";
         AITool summarizerTool = AIFunctionFactory.Create(aiSearchAdmin.SearchIndexAsync);
         summarizerAgent = await GetFoundryAgent(summarizerName, [summarizerTool]);
         if (summarizerAgent == null)
         {
            summarizerAgent = await CreateFoundryAgent(summarizerName, openAiChatDeploymentName, "Summarizes document content", SummarizerInstructions, [summarizerTool]);
         }
         if (summarizerAgent == null)
         {
            throw new NullReferenceException("The Summarizer agent failed to initialize!");
         }

         // Initialize Cross-Document agent
         var crossDocName = "CrossDocument";
         AITool crossDocTool = AIFunctionFactory.Create(aiSearchAdmin.SearchAllDocumentsAsync);
         crossDocumentAgent = await GetFoundryAgent(crossDocName, [crossDocTool]);
         if (crossDocumentAgent == null)
         {
            crossDocumentAgent = await CreateFoundryAgent(crossDocName, openAiChatDeploymentName, "Answers questions across all documents", CrossDocumentInstructions, [crossDocTool]);
         }
         if (crossDocumentAgent == null)
         {
            throw new NullReferenceException("The CrossDocument agent failed to initialize!");
         }

         // Initialize Router agent with tools that delegate to specialist agents
         var routerName = "Router";
         AITool routerAskSingleTool = AIFunctionFactory.Create(AskSingleDocumentForRouter);
         AITool routerAskCrossTool = AIFunctionFactory.Create(AskCrossDocumentForRouter);
         AITool routerSummarizeTool = AIFunctionFactory.Create(SummarizeDocumentForRouter);
         routerAgent = await GetFoundryAgent(routerName, [routerAskSingleTool, routerAskCrossTool, routerSummarizeTool]);
         if (routerAgent == null)
         {
            routerAgent = await CreateFoundryAgent(routerName, openAiChatDeploymentName, "Routes questions to the appropriate specialist agent", RouterInstructions, [routerAskSingleTool, routerAskCrossTool, routerSummarizeTool]);
         }
         if (routerAgent == null)
         {
            throw new NullReferenceException("The Router agent failed to initialize!");
         }
      }

      private async Task<AIAgent?> GetFoundryAgent(string agentName, params AITool[] tools)
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
         return (AIAgent)(await foundryProjectClient.GetAIAgentAsync(agentName, tools.ToList(), clientFactory: null, services: null))
               .AsBuilder()
               .UseOpenTelemetry(sourceName: Constants.TRACE_SOURCE_NAME, configure: cfg =>
               {
                  cfg.EnableSensitiveData = true;
               })
               .Build();
      }

      private async Task<AIAgent?>  CreateFoundryAgent(string name, string deployment, string description, string instructions, params AITool[] tools)
      {
         try
         {
            var agent = await foundryProjectClient.CreateAIAgentAsync(name: name, model: deployment, instructions: instructions, description: description, tools: tools.ToList(), clientFactory: null, services: null);
            return (AIAgent)agent
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
            session = await askQuestionsAgent.CreateSessionAsync();
         }

         await foreach (AgentResponseUpdate update in askQuestionsAgent.RunStreamingAsync(new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session))
         {
            if (update.Text != null)
            {
               yield return (update.ToString(), session);
            }
         }

      }

      public async IAsyncEnumerable<(string text, AgentSession session)> SummarizeDocumentStreamingAsync(string fileName, AgentSession? session = null)
      {
         log.LogDebug("Summarizing document...");

         string userMessage = $"Document Name:\n{fileName}\n\nPlease provide a comprehensive summary of this document.";
         if (session == null)
         {
            session = await summarizerAgent.CreateSessionAsync();
         }

         await foreach (AgentResponseUpdate update in summarizerAgent.RunStreamingAsync(new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session))
         {
            if (update.Text != null)
            {
               yield return (update.ToString(), session);
            }
         }
      }

      public async IAsyncEnumerable<(string text, AgentSession session)> AskCrossDocumentStreamingAsync(string question, AgentSession? session = null)
      {
         log.LogDebug("Asking cross-document question...");

         string userMessage = $"Question: {question}";
         if (session == null)
         {
            session = await crossDocumentAgent.CreateSessionAsync();
         }

         await foreach (AgentResponseUpdate update in crossDocumentAgent.RunStreamingAsync(new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session))
         {
            if (update.Text != null)
            {
               yield return (update.ToString(), session);
            }
         }
      }

      public async IAsyncEnumerable<(string text, AgentSession session)> RouteQuestionStreamingAsync(string question, string activeDocument, AgentSession? session = null)
      {
         log.LogDebug("Routing question through Router agent...");

         string context = string.IsNullOrWhiteSpace(activeDocument)
            ? "No active document is currently set."
            : $"Active Document: {activeDocument}";
         string userMessage = $"{context}\n\nUser Request: {question}";

         if (session == null)
         {
            session = await routerAgent.CreateSessionAsync();
         }

         await foreach (AgentResponseUpdate update in routerAgent.RunStreamingAsync(new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session))
         {
            if (update.Text != null)
            {
               yield return (update.ToString(), session);
            }
         }
      }

      // Router tool delegate methods
      [Description("Ask a question about a specific document. Use when the user asks about their active document.")]
      private string AskSingleDocumentForRouter([Description("The question to ask")] string question, [Description("The document name")] string documentName)
      {
         var results = aiSearchAdmin.SearchIndexAsync(documentName, question);
         if (results.Count == 0) return "No relevant information found in the specified document.";
         return string.Join("\n\n", results.Select(r => $"[{r.FileName}]: {r.Content}"));
      }

      [Description("Search across all available documents to answer a question. Use when no specific document is mentioned or user wants cross-document answers.")]
      private string AskCrossDocumentForRouter([Description("The question to ask")] string question)
      {
         var results = aiSearchAdmin.SearchAllDocumentsAsync(question);
         if (results.Count == 0) return "No relevant information found across any documents.";
         return string.Join("\n\n", results.Select(r => $"[{r.FileName}]: {r.Content}"));
      }

      [Description("Summarize a specific document. Use when the user asks for a summary or overview.")]
      private string SummarizeDocumentForRouter([Description("The document name to summarize")] string documentName)
      {
         var results = aiSearchAdmin.SearchIndexAsync(documentName, "provide a comprehensive overview and summary of the entire document");
         if (results.Count == 0) return "No content found for the specified document.";
         return string.Join("\n\n", results.Select(r => $"[{r.FileName}]: {r.Content}"));
      }
   }

   public sealed record SemanticMemoryResult(string? Id, string? FileName, string? Content, double? Score);

   public enum AgentStatus
   {
      New,
      Preexisting
   }
}

