using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

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
            var key = config.GetValue<string>(Constants.DOCUMENTINTELLIGENCE_KEY) ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_KEY} in configuration");
            this.docIntelClient = new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(key));
         }catch(Exception exe)
         {
            log.LogError(exe.ToString() );
         }
      }


      public async Task ProcessDocument(Uri fileUri, string modelId = "prebuilt-layout")
      {
         //log.LogInformation($"Processing file {file.FullName} with Document Intelligence Service...");
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
         //log.LogInformation($"Processing file {file.FullName} with Document Intelligence Service...");
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
            //taskList.Add(aiSearch.StoreDataInIndex(indexName, Common.BaseFileName(filePathOrUrl), chunked));
            taskList.Add(aiSearch.StoreDataInIndex(AiSearch.IndexName, fileName, chunked));
            Task.WaitAll(taskList.ToArray());
         }
         log.LogInformation("Document Processed and Indexed");

      }

   }
}
