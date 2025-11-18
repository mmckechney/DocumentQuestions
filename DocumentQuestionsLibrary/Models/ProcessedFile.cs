using System.Text.Json.Serialization;

namespace DocumentQuestions.Library.Models
{
   public class ProcessedFile
   {
      [JsonPropertyName("fileName")]
      public string FileName { get; set; }

      [JsonPropertyName("blobName")]
      public string BlobName { get; set; }

      [JsonPropertyName("content")]
      public string Content { get; set; }
   }
}
