using System;

namespace DocumentQuestions.Library.Models
{
   public class MemoryQueryResult
   {
      public MemoryMetadata Metadata { get; set; } = new();
      public double Relevance { get; set; }
   }

   public class MemoryMetadata
   {
      public string Id { get; set; } = string.Empty;
      public string Description { get; set; } = string.Empty;
      public string ExternalSourceName { get; set; } = string.Empty;
   }
}
