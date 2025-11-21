using Azure;
using Azure.AI.OpenAI;

//using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using System.ClientModel.Primitives;
using System.Collections;
using System.ComponentModel;
using aim = Azure.Search.Documents.Indexes.Models;

namespace DocumentQuestions.Library
{
   public class AiSearch
   {

      ILogger<AiSearch> log;
      IConfiguration config;

      private const string VectorFieldName = "contentVector";
      private const string ContentFieldName = "content";
      private const string FileNameFieldName = "fileName";
      private const string IdFieldName = "id";
      public const string IndexName = "general";
      public const int EmbeddingChunkSize = 7000;
      public const int MaxItemReturnCount = 10;
      private IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;
      private SearchIndexClient indexClient;
      private EmbeddingClient embeddingClient;
      private Uri searchEndpointUri;
      private AzureKeyCredential searchCredential;
      public AiSearch(ILogger<AiSearch> log, IConfiguration config)
      {
         this.log = log;
         this.config = config;
         string endpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration");
         this.searchEndpointUri = new Uri(endpoint);
         string key = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration");

         // Create a client
         this.searchCredential = new AzureKeyCredential(key);
         indexClient = new SearchIndexClient(searchEndpointUri, searchCredential);


         var embeddingModel = config[Constants.OPENAI_EMBEDDING_MODEL_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_MODEL_NAME} in configuration.");
         var embeddingDeploymentName = config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME] ?? throw new ArgumentException($"Missing {Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME} in configuration.");


         AIProjectClient foundryClient  =  new AIProjectClient(new Uri(config[Constants.AIFOUNDRY_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AIFOUNDRY_ENDPOINT} in configuration.")), new DefaultAzureCredential());


         ClientConnection connection = foundryClient.GetConnection(typeof(AzureOpenAIClient).FullName!);
         if (!connection.TryGetLocatorAsUri(out Uri uri) || uri is null)
         {
            throw new InvalidOperationException("Invalid URI.");
         }
         uri = new Uri($"https://{uri.Host}");
         AzureOpenAIClient azureOpenAIClient = new AzureOpenAIClient(uri, new DefaultAzureCredential());
         embeddingClient = azureOpenAIClient.GetEmbeddingClient(embeddingDeploymentName);
         embeddingGenerator = embeddingClient.AsIEmbeddingGenerator();


      }


      public async Task StoreDataInIndex(string collectionName, string filename, IEnumerable<string> contents, CancellationToken cancellationToken = default)
      {

         await this.AddIndex(AiSearch.IndexName);
         log.LogInformation($"Storing memory to AI Search index '{AiSearch.IndexName}'...");

         var client = new SearchClient(searchEndpointUri, AiSearch.IndexName, this.searchCredential);
     
         var documents = new List<SearchDocument>();
         var index = 0;

         foreach (var entry in contents)
         {
            if (string.IsNullOrWhiteSpace(entry))
            {
               log.LogWarning($"The contents of {filename} was empty. Unable to save to the index {AiSearch.IndexName}");
               continue;
            }

            var embedding = await embeddingGenerator!.GenerateAsync(entry, cancellationToken: cancellationToken).ConfigureAwait(false);
            var docId = Common.ReplaceInvalidCharacters($"{filename}_{index++:D6}");
            var vector = embedding.Vector.ToArray();

            var document = new SearchDocument
            {
               [IdFieldName] = docId,
               [FileNameFieldName] = filename,
               [ContentFieldName] = entry,
               [VectorFieldName] = vector
            };

            documents.Add(document);
         }

         if (documents.Count == 0)
         {
            log.LogWarning($"No documents generated for {filename} in index {AiSearch.IndexName}");
            return;
         }

         await client.MergeOrUploadDocumentsAsync(documents, cancellationToken: cancellationToken).ConfigureAwait(false);
         log.LogInformation($"{documents.Count} entries saved to {AiSearch.IndexName}.");
      }

      [Description("Searches AI Search index for information from the specified document and the provided query.")]
      public IReadOnlyList<SemanticMemoryResult> SearchIndexAsync([Description("The name of the file to filter search.")] string fileName, [Description("The search query.")] string query, CancellationToken cancellationToken = default)
      {
         var searchResult = Task.Run(async () =>
         {
            log.LogDebug("\nQuery: {Query}\n", query);
            log.LogDebug("FileName Filter: {FileName}\n", fileName);

            // Use the general index, not the fileName as the index name
            var client = new SearchClient(searchEndpointUri, AiSearch.IndexName, this.searchCredential);

            var embedding = await embeddingGenerator!.GenerateAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
            var vectorQuery = new VectorizedQuery(embedding.Vector.ToArray())
            {
               KNearestNeighborsCount = MaxItemReturnCount,
               Fields = { VectorFieldName }
            };

            var options = new SearchOptions
            {
               Size = MaxItemReturnCount,
               QueryType = SearchQueryType.Semantic, 
               VectorSearch = new VectorSearchOptions(),
               SemanticSearch = new SemanticSearchOptions
               {
                  SemanticConfigurationName = "semantics",
                  QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                  QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
               },
               Debug = new QueryDebugMode()
            };
            
            // Add filter to restrict results to specific fileName
            if (!string.IsNullOrWhiteSpace(fileName))
            {
               // OData filter syntax for exact match on fileName field
               options.Filter = $"{FileNameFieldName} eq '{fileName.Replace("'", "''")}'"; // Escape single quotes
               log.LogDebug("Applied filter: {Filter}", options.Filter);
            }
            
            options.VectorSearch.Queries.Add(vectorQuery);
            options.Select.Add(IdFieldName);
            options.Select.Add(ContentFieldName);
            options.Select.Add(FileNameFieldName);
            options.IncludeTotalCount = true;

            var response = await client.SearchAsync<SearchDocument>(null, options, cancellationToken).ConfigureAwait(false);

            var results = new List<SemanticMemoryResult>();
            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
               var content = result.Document.TryGetValue(ContentFieldName, out var textObj) ? textObj as string : null;
               var fileNameValue = result.Document.TryGetValue(FileNameFieldName, out var fileObj) ? fileObj as string : null;
               var id = result.Document.TryGetValue(IdFieldName, out var idObj) ? idObj as string : null;
               results.Add(new SemanticMemoryResult(id, fileNameValue, content, result.Score));

               log.LogDebug("Result {Index}:\n  Id: {Id}\n  File: {File}\n  Score: {Score}", results.Count, id, fileNameValue, result.Score);
            }

            log.LogDebug("Found {Count} results after filtering", results.Count);
            log.LogDebug("----------------------");
            return results;
         }).GetAwaiter().GetResult();

         return searchResult;
      }
      
      public async Task<List<string>> ListAvailableIndexes(bool unquoted = false)
      {
         try
         {
            List<string> names = new();
            await foreach (var page in indexClient.GetIndexNamesAsync())
            {
               if (unquoted)
               {
                  names.Add(page);
               }
               else
               {
                  names.Add($"\"{page}\"");
               }
            }
            return names;
         }
         catch (Exception exe)
         {
            log.LogError($"Problem retrieving AI Search Idexes:\r\n{exe.Message}");
            return new List<string>();
         }
      }
     
      public async Task<string> AddIndex(string name)
      {
         try
         {
            // Sanitize name for Azure Search
            name = Common.ReplaceInvalidCharacters(name);

            // If index already exists, return without creating
            try
            {
               var existing = await indexClient.GetIndexAsync(name);
               if (existing != null)
               {
                  log.LogInformation("Index {IndexName} already exists.", name);
                  return name;
               }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
               // Expected when index does not exist; proceed to create
            }

            const string vectorProfileName = "v1-hnsw"; // referenced by field
            const int embeddingDimensions = 3072; // adjust for different embedding models

            var hnsw = new HnswParameters
            {
               M = 30,                // graph degree (typical 16-48)
               EfConstruction = 400,  // construction search depth
               EfSearch = 100,        // query-time search depth
               Metric = VectorSearchAlgorithmMetric.Cosine
            };


            var vectorAlgo = new aim.HnswAlgorithmConfiguration("hnsw-algo")
            {

               Parameters = new HnswParameters
               {
                  M = 30,                // Number of bi-directional links per node (graph degree)
                  EfConstruction = 400,  // Size of dynamic candidate list during index construction
                  EfSearch = 100,        // Size of dynamic candidate list during search
                  Metric = VectorSearchAlgorithmMetric.Cosine // Similarity metric (Cosine, Euclidean, DotProduct)
               }
            };



            var vectorProfile = new VectorSearchProfile(
                      name: vectorProfileName,
                      algorithmConfigurationName: vectorAlgo.Name);


            var vectorSearch = new VectorSearch
            {
               Algorithms = { vectorAlgo },
               Profiles = { vectorProfile },
            };


            var semanticConfig = new SemanticConfiguration(
                name: "semantics",
                new SemanticPrioritizedFields
                {
                   TitleField = new SemanticField("fileName"),
                   ContentFields = { new SemanticField("content") }
                });

            var semanticSettings = new SemanticSearch();
            semanticSettings.Configurations.Add(semanticConfig);


            var index = new aim.SearchIndex(name)
            {
               Fields =
               {
                  new aim.SimpleField("id", aim.SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                  new aim.SimpleField("fileName", aim.SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                  new aim.SearchField("content", aim.SearchFieldDataType.String) { IsSearchable = true, AnalyzerName = aim.LexicalAnalyzerName.EnMicrosoft },
                  new aim.SearchField("contentVector", aim.SearchFieldDataType.Collection(aim.SearchFieldDataType.Single))
                  {
                     VectorSearchDimensions = embeddingDimensions,
                     VectorSearchProfileName = vectorProfileName,
                     IsFilterable = false,
                     IsFacetable = false,
                     IsSortable = false,
                     IsSearchable = true
                  }
               },
               VectorSearch = vectorSearch,
               SemanticSearch = semanticSettings
            };

            await indexClient.CreateOrUpdateIndexAsync(index);
            log.LogInformation("Created vector-enabled index {IndexName}.", name);
            return name;
         }
         catch (Exception exe)
         {
            log.LogError($"Problem creating AI Search Index {name}:\r\n{exe.Message}");
            return "";
         }
      }

      public async Task<List<string>> ClearIndexes(List<string> indexNames)
      {
         List<string> deleted = new();
         var available = await ListAvailableIndexes(true);

         foreach (var index in indexNames)
         {
            if (available.Contains(index, StringComparer.CurrentCultureIgnoreCase))
            {
               try
               {
                  var result = await indexClient.DeleteIndexAsync(index);
                  if (result.Status < 300)
                  {
                     deleted.Add(index);
                  }
                  else
                  {
                     log.LogError($"Problem deleting index {index}:\r\n{result.ReasonPhrase}");
                  }
               }
               catch (Exception exe)
               {
                  log.LogError($"Problem deleting index {index}:\r\n{exe.Message}");
               }
            }
            else
            {
               log.LogWarning($"The file index '{index}' was not found.");
            }
         }
         return deleted;
      }

      public async Task<IReadOnlyList<string>> GetDistinctFileNamesAsync(string? filter = null, int maxDistinct = 1000)
      {
         await AddIndex(AiSearch.IndexName);
         // We only need facets, not actual documents
         var options = new SearchOptions
         {
            Size = 50 // don't return documents
         };

         // Optional: apply a filter to narrow the scope before faceting
         if (!string.IsNullOrWhiteSpace(filter))
         {
            options.Filter = filter; // e.g., "category eq 'Contracts'"
         }

         // Facet syntax: "<field>[,count:<N>]"
         // Default facet count is 10; increase if you need more (max ~1000).
         options.Facets.Add($"fileName,count:{maxDistinct}");

         // Use "*" to match all docs (subject to filter)
         var client = new SearchClient(searchEndpointUri, AiSearch.IndexName, this.searchCredential);
         var response = await client.SearchAsync<SearchDocument>("*", options);

         // Extract distinct values from the facet results
         if (response.Value.Facets != null &&
             response.Value.Facets.TryGetValue("fileName", out IList<FacetResult> facetValues) &&
             facetValues != null)
         {
            // Each FacetResult.Value is the distinct term for the field
            var distinct = facetValues
                .Select(f => f.Value?.ToString())   // Value is the term
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return distinct;
         }

         return Array.Empty<string>();
      }
   }

}
