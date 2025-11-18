using System;
using System.Collections.Generic;
using System.Linq;

namespace DocumentQuestions.Library
{
   /// <summary>
   /// Simple text chunker utility (replacement for SK TextChunker)
   /// </summary>
   public static class TextChunker
   {
      /// <summary>
      /// Split text into paragraphs with a maximum token count
      /// </summary>
      public static List<string> SplitPlainTextParagraphs(List<string> lines, int maxTokensPerParagraph)
      {
         var chunks = new List<string>();
         var currentChunk = new List<string>();
         var currentLength = 0;

         foreach (var line in lines)
         {
            // Rough token estimate: ~4 characters per token
            var lineTokens = line.Length / 4;

            if (currentLength + lineTokens > maxTokensPerParagraph && currentChunk.Count > 0)
            {
               // Add current chunk and start a new one
               chunks.Add(string.Join(Environment.NewLine, currentChunk));
               currentChunk.Clear();
               currentLength = 0;
            }

            currentChunk.Add(line);
            currentLength += lineTokens;
         }

         // Add the last chunk if it has content
         if (currentChunk.Count > 0)
         {
            chunks.Add(string.Join(Environment.NewLine, currentChunk));
         }

         return chunks;
      }
   }
}
