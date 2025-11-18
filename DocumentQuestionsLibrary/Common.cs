using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using DocumentQuestions.Library.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocumentQuestions.Library
{
   public class Common
   {
      ILogger<Common> log;
      IConfiguration config;
      public Common(ILogger<Common> log, IConfiguration config)
      {
         this.log = log;
         this.config = config;
      }

      private static TokenCredential _tokenCred = null;
      public static TokenCredential EntraTokenCredential
      {
         get
         {
            if(_tokenCred == null)
            {
               _tokenCred = new ChainedTokenCredential(new ManagedIdentityCredential(), new AzureCliCredential());
            }
            return _tokenCred;

         }
      }

      public static string GetFileNameForBlob(string filePathOrUrl)
      {
            string fileName;
            Uri uri;
            if (Uri.TryCreate(filePathOrUrl, UriKind.RelativeOrAbsolute, out uri) && uri.IsAbsoluteUri && uri.Scheme != Uri.UriSchemeFile)
            {
               // It's a URL
               fileName =  Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            }
            else
            {
               // It's a local file path
               fileName = Path.GetFileNameWithoutExtension(filePathOrUrl);
            }
         return fileName;
      }

      public static string BaseFileName(string filePathOrUrl)
      {
         string fileName;
         Uri uri;
         if (Uri.TryCreate(filePathOrUrl, UriKind.RelativeOrAbsolute, out uri) && uri.IsAbsoluteUri && uri.Scheme != Uri.UriSchemeFile)
         {
            // It's a URL
            fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
         }
         else
         {
            // It's a local file path
            fileName = Path.GetFileNameWithoutExtension(filePathOrUrl);
         }
         return fileName;
      }

      public static string ReplaceInvalidCharacters(string input)
      {
         input = Path.GetFileNameWithoutExtension(input).ToLower();
         // Replace any characters that are not letters, digits, or dashes with a dash
         string result = Regex.Replace(input, @"[^a-zA-Z0-9-]", "-");

         // Remove any trailing dashes
         result = Regex.Replace(result, @"-+$", "");
         if (result.Length > 128) result = result.Substring(0, 128);
         return result;
      }

      public async Task<string> GetBlobContentAsync(string blobName)
      {
         string storageURL = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
         string containerName = config[Constants.EXTRACTED_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.EXTRACTED_CONTAINER_NAME} in configuration.");



         BlobServiceClient blobServiceClient = new BlobServiceClient(new Uri(storageURL), new DefaultAzureCredential());
         BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

         var blobs = containerClient.GetBlobs(prefix: blobName);
         log.LogInformation($"Number of blobs {blobs.Count()}");

         var content = "";
         foreach (var blob in blobs)
         {
            blobName = blob.Name;

            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            // Open the blob and read its contents.  
            using (Stream stream = await blobClient.OpenReadAsync())
            {
               using (StreamReader reader = new StreamReader(stream))
               {
                  var processedFile = JsonSerializer.Deserialize<ProcessedFile>(await reader.ReadToEndAsync());
                  content += processedFile.Content;


               }
            }

         }
         return content;
      }

      public async Task<Dictionary<string, string>> GetBlobContentDictionaryAsync(string blobName)
      {
         string storageURL = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
         string containerName = config[Constants.EXTRACTED_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.EXTRACTED_CONTAINER_NAME} in configuration.");



         BlobServiceClient blobServiceClient = new BlobServiceClient(new Uri(storageURL), new DefaultAzureCredential());
         BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

         var blobs = containerClient.GetBlobs(prefix: blobName);
         log.LogInformation($"Number of blobs {blobs.Count()}");

         var content = "";
         Dictionary<string, string> docFile = new();

         foreach (var blob in blobs)
         {

            blobName = blob.Name;

            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            // Open the blob and read its contents.  
            using (Stream stream = await blobClient.OpenReadAsync())
            {
               using (StreamReader reader = new StreamReader(stream))
               {
                  content += await reader.ReadToEndAsync();
                  docFile.Add(blob.Name, content);
               }
            }

         }
         return docFile;
      }


      public string GetFileName(string name)
      {
         string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
         string newName = nameWithoutExtension.Replace(".", "_");
         newName += $".md";
         return newName;
      }

      public async Task<bool> WriteAnalysisContentToBlob(string name, string content, ILogger log)
      {
         string newName = GetFileName(name);
         string blobName = Path.GetFileNameWithoutExtension(name) + "/" + newName;
         try
         {
        
            string storageURL = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
            string containerName = config[Constants.EXTRACTED_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.EXTRACTED_CONTAINER_NAME} in configuration.");

            var blobServiceClient = new BlobServiceClient(new Uri(storageURL), new DefaultAzureCredential());
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            containerClient.CreateIfNotExists();

            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            using (var stream = new MemoryStream())
            {
               byte[] contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
               stream.Write(contentBytes, 0, contentBytes.Length);
               stream.Seek(0, SeekOrigin.Begin);
               await blobClient.UploadAsync(stream, overwrite: true);

            }

            log.LogInformation($"Markdown file {newName} saved to Azure Blob Storage.");
            return true;

         }
         catch (Exception exe)
         {
            log.LogError($"Unable to save file {name} to blob {blobName}: {exe.Message}");
            return false;
         }
      }
   }
}
