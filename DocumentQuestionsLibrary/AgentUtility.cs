using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using System.ClientModel;
using System.ComponentModel;
using System.Text;


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
1. ask_single_document - Delegates to the AskQuestions specialist agent for questions about a specific document.
2. ask_cross_document - Delegates to the CrossDocument specialist agent for questions spanning all documents.
3. summarize_document - Delegates to the Summarizer specialist agent for document summaries.

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
         // Telemetry is now configured via OpenTelemetry integration in agent builders
      }

      public async Task InitAgents()
      {
         var openAiChatDeploymentName = config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_CHAT_DEPLOYMENT_NAME} in configuration.");

         // Initialize AskQuestions agent (single-document)
         var askAgentName = "AskQuestions";
         AITool askTool = AIFunctionFactory.Create(aiSearchAdmin.SearchIndexAsync);
         askQuestionsAgent = await GetOrCreateAgent(askAgentName, openAiChatDeploymentName, "Asks questions about the document", AskQuestionsInstructions, askTool);

         // Initialize Summarizer agent
         var summarizerName = "Summarizer";
         AITool summarizerTool = AIFunctionFactory.Create(aiSearchAdmin.SearchIndexAsync);
         summarizerAgent = await GetOrCreateAgent(summarizerName, openAiChatDeploymentName, "Summarizes document content", SummarizerInstructions, summarizerTool);

         // Initialize Cross-Document agent
         var crossDocName = "CrossDocument";
         AITool crossDocTool = AIFunctionFactory.Create(aiSearchAdmin.SearchAllDocumentsAsync);
         crossDocumentAgent = await GetOrCreateAgent(crossDocName, openAiChatDeploymentName, "Answers questions across all documents", CrossDocumentInstructions, crossDocTool);

         // Initialize Router agent with tools that delegate to specialist agents
         var routerName = "Router";
         AITool routerAskSingleTool = AIFunctionFactory.Create(AskSingleDocumentForRouter);
         AITool routerAskCrossTool = AIFunctionFactory.Create(AskCrossDocumentForRouter);
         AITool routerSummarizeTool = AIFunctionFactory.Create(SummarizeDocumentForRouter);
         routerAgent = await GetOrCreateAgent(routerName, openAiChatDeploymentName, "Routes questions to the appropriate specialist agent", RouterInstructions, routerAskSingleTool, routerAskCrossTool, routerSummarizeTool);
      }

      private async Task<AIAgent> GetOrCreateAgent(string agentName, string deployment, string description, string instructions, params AITool[] tools)
      {
         try
         {
            var record = await foundryProjectClient.AgentAdministrationClient.GetAgentAsync(agentName);
            log.LogInformation("Found existing agent '{AgentName}'", agentName);
            return (AIAgent)foundryProjectClient.AsAIAgent(agentRecord: record, tools: tools.ToList())
               .AsBuilder()
               .UseOpenTelemetry(sourceName: Constants.TRACE_SOURCE_NAME, configure: cfg =>
               {
                  cfg.EnableSensitiveData = true;
               })
               .Build();
         }
         catch (Exception ex) when (
            ex is ClientResultException crEx && crEx.Status == 404 ||
            ex is Azure.RequestFailedException rfEx && rfEx.Status == 404 ||
            ex is InvalidOperationException)
         {
            log.LogInformation("Agent '{AgentName}' not found, creating code-first agent", agentName);
            return (AIAgent)foundryProjectClient.AsAIAgent(
               model: deployment,
               instructions: instructions,
               name: agentName,
               description: description,
               tools: tools.ToList())
               .AsBuilder()
               .UseOpenTelemetry(sourceName: Constants.TRACE_SOURCE_NAME, configure: cfg =>
               {
                  cfg.EnableSensitiveData = true;
               })
               .Build();
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

      /// <summary>
      /// Sequential agent-to-agent workflow: CrossDocument agent searches across all docs,
      /// then Summarizer agent condenses the findings into a concise answer.
      /// Uses AgentWorkflowBuilder.BuildSequential for true multi-agent orchestration.
      /// </summary>
      public async IAsyncEnumerable<string> SearchAndSummarizeStreamingAsync(string question)
      {
         log.LogDebug("Running sequential workflow: CrossDocument → Summarizer...");

         var workflow = AgentWorkflowBuilder.BuildSequential("SearchAndSummarize", [crossDocumentAgent, summarizerAgent]);

         var messages = new List<ChatMessage>
         {
            new() { Contents = [new TextContent(question)], Role = ChatRole.User }
         };

         StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, messages);
         await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

         await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
         {
            if (evt is AgentResponseUpdateEvent e && e.Data != null)
            {
               yield return e.Data.ToString();
            }
            else if (evt is WorkflowOutputEvent)
            {
               break;
            }
         }
      }

      // Router tool delegate methods — these invoke the actual specialist agents (true agent-to-agent delegation)
      [Description("Delegates to the AskQuestions specialist agent to answer a question about a specific document.")]
      private string AskSingleDocumentForRouter([Description("The question to ask")] string question, [Description("The document name")] string documentName)
      {
         log.LogDebug("Router delegating to AskQuestions agent for document: {Document}", documentName);
         var session = askQuestionsAgent.CreateSessionAsync().GetAwaiter().GetResult();
         string userMessage = $"Document Name:\n{documentName}\n\nQuestion: {question}";

         StringBuilder responseBuilder = new();
         var updates = askQuestionsAgent.RunStreamingAsync(
            new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session);

         // Collect the full response from the specialist agent
         var enumerator = updates.GetAsyncEnumerator();
         while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
         {
            if (enumerator.Current.Text != null)
            {
               responseBuilder.Append(enumerator.Current.ToString());
            }
         }

         return responseBuilder.Length > 0 ? responseBuilder.ToString() : "The AskQuestions agent could not find an answer.";
      }

      [Description("Delegates to the CrossDocument specialist agent to search across all documents and answer a question.")]
      private string AskCrossDocumentForRouter([Description("The question to ask")] string question)
      {
         log.LogDebug("Router delegating to CrossDocument agent...");
         var session = crossDocumentAgent.CreateSessionAsync().GetAwaiter().GetResult();
         string userMessage = $"Question: {question}";

         StringBuilder responseBuilder = new();
         var updates = crossDocumentAgent.RunStreamingAsync(
            new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session);

         var enumerator = updates.GetAsyncEnumerator();
         while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
         {
            if (enumerator.Current.Text != null)
            {
               responseBuilder.Append(enumerator.Current.ToString());
            }
         }

         return responseBuilder.Length > 0 ? responseBuilder.ToString() : "The CrossDocument agent could not find an answer.";
      }

      [Description("Delegates to the Summarizer specialist agent to summarize a specific document.")]
      private string SummarizeDocumentForRouter([Description("The document name to summarize")] string documentName)
      {
         log.LogDebug("Router delegating to Summarizer agent for document: {Document}", documentName);
         var session = summarizerAgent.CreateSessionAsync().GetAwaiter().GetResult();
         string userMessage = $"Document Name:\n{documentName}\n\nPlease provide a comprehensive summary of this document.";

         StringBuilder responseBuilder = new();
         var updates = summarizerAgent.RunStreamingAsync(
            new ChatMessage() { Contents = [new TextContent(userMessage)], Role = ChatRole.User }, session);

         var enumerator = updates.GetAsyncEnumerator();
         while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
         {
            if (enumerator.Current.Text != null)
            {
               responseBuilder.Append(enumerator.Current.ToString());
            }
         }

         return responseBuilder.Length > 0 ? responseBuilder.ToString() : "The Summarizer agent could not produce a summary.";
      }
   }

   public sealed record SemanticMemoryResult(string? Id, string? FileName, string? Content, double? Score);

   public enum AgentStatus
   {
      New,
      Preexisting
   }
}

