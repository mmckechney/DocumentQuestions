using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DocumentQuestions.Library
{

   public class DocumentIntelligence
   {
      public static List<string> ModelList =
            [ "prebuild-layout",
               "prebuilt-read",
               "prebuilt-mortgage.us.1003",
               "prebuilt-mortgage.us.1004",
               "prebuilt-mortgage.us.1005",
               "prebuilt-mortgage.us.1008",
               "prebuilt-mortgage.us.closingDisclosure",
               "prebuilt-tax.us",
               "prebuilt-idDocument"
           ];

      private DocumentIntelligenceClient docIntelClient;
      //private ILoggerFactory logFactory;
      private ILogger<DocumentIntelligence> log;
      private IConfiguration config;
      private AgentUtility agentUtility;
      private Common common;
      private AiSearch aiSearch;
      public DocumentIntelligence(ILogger<DocumentIntelligence> log, IConfiguration config, AgentUtility agentUtility, AiSearch aiSearch, Common common)
      {
         this.log = log;
         this.config = config;
         this.agentUtility = agentUtility;
         this.common = common;
         this.aiSearch = aiSearch;

         try
         {
            var endpoint = config.GetValue<Uri>(Constants.DOCUMENTINTELLIGENCE_ENDPOINT) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_ENDPOINT} in configuration");
            this.docIntelClient = new DocumentIntelligenceClient(endpoint, new DefaultAzureCredential());
         }
         catch (Exception exe)
         {
            log.LogError(exe.ToString());
         }
      }


      public async Task ProcessDocument(Uri fileUri, string modelId = "prebuilt-layout")
      {
         Operation<AnalyzeResult> operation;

         log.LogInformation($"Analyzing document with model ID: {modelId} ");
         AnalyzeDocumentOptions opts = new AnalyzeDocumentOptions(modelId: modelId, uriSource: fileUri)
         {
            OutputContentFormat = DocumentContentFormat.Markdown
         };
         operation = await docIntelClient.AnalyzeDocumentAsync(Azure.WaitUntil.Completed, opts);
         AnalyzeResult result = operation.Value;
         await ProcessDocumentResults(result, fileUri.AbsoluteUri);
      }

      public async Task ProcessDocument(FileInfo file, string modelId = "prebuilt-layout")
      {
         Operation<AnalyzeResult> operation;

         using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
         {
            log.LogInformation($"Analyzing document with model ID: {modelId} ");
            BinaryData binaryDoc = BinaryData.FromStream(stream);
            AnalyzeDocumentOptions opts = new AnalyzeDocumentOptions(modelId: modelId, bytesSource: binaryDoc)
            {
               OutputContentFormat = DocumentContentFormat.Markdown
            };
            operation = await docIntelClient.AnalyzeDocumentAsync(Azure.WaitUntil.Completed, opts);
         }
         AnalyzeResult result = operation.Value;

         await ProcessDocumentResults(result, file.FullName);
      }

      public async Task ProcessDocumentResults(AnalyzeResult result, string filePathOrUrl)
      {
         if (result != null)
         {
            var fileName = Common.GetFileNameForBlob(filePathOrUrl);
            string content = result.Content;
            var contentLines = content.Split("\n").ToList();


            log.LogInformation($"Writing document Markdown to blob...");
            await common.WriteAnalysisContentToBlob(fileName, result.Content, log);
            log.LogInformation($"Parsing Document Intelligence results...");
            var chunked = TextChunker.SplitPlainTextParagraphs(contentLines, AiSearch.EmbeddingChunkSize);
            var taskList = new List<Task>();

            log.LogInformation($"Saving Document Intelligence results to Azure AI Search Index...");
            taskList.Add(aiSearch.StoreDataInIndex(AiSearch.IndexName, fileName, chunked));
            Task.WaitAll(taskList.ToArray());

            // Auto-summarize the document after indexing
            log.LogInformation($"Generating document summary...");
            try
            {
               StringBuilder summaryBuilder = new();
               await foreach (var (text, session) in agentUtility.SummarizeDocumentStreamingAsync(fileName))
               {
                  summaryBuilder.Append(text);
               }

               if (summaryBuilder.Length > 0)
               {
                  var summaryChunked = TextChunker.SplitPlainTextParagraphs(
                     summaryBuilder.ToString().Split("\n").ToList(), AiSearch.EmbeddingChunkSize);
                  await aiSearch.StoreDataInIndex(AiSearch.IndexName, $"summary-{fileName}", summaryChunked);
                  log.LogInformation($"Document summary generated and indexed.");
               }
            }
            catch (Exception exe)
            {
               log.LogWarning($"Auto-summarization completed with warning: {exe.Message}");
            }
         }
         log.LogInformation("Document Processed and Indexed");

      }

   }
}
