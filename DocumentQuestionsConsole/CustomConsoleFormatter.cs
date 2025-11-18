
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using syS = System;
namespace DocumentQuestions.Console
{

   public sealed class CustomConsoleFormatter : ConsoleFormatter
   {
      public CustomConsoleFormatter() : base("custom")
      {
      }

      public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
      {
         (var color, var level) = LogLevelShort(logEntry.LogLevel);

         var messages = logEntry.State.ToString().Split("|", StringSplitOptions.RemoveEmptyEntries);
         string parsedMessage = "";

         if (logEntry.LogLevel != LogLevel.Information)
         {
            syS.Console.Write("[");
            syS.Console.ForegroundColor = color;
            syS.Console.Write($"{level}");
            syS.Console.ResetColor();
            syS.Console.Write("] ");
         }
         foreach (var msg in messages)
         {
            (syS.Console.ForegroundColor, parsedMessage) = GetLogEntryColor(msg);
            syS.Console.Write($"{parsedMessage} ");
            syS.Console.ResetColor();
         }
         syS.Console.WriteLine();

      }
      private (syS.ConsoleColor, string) LogLevelShort(LogLevel level)
      {
         switch (level)
         {
            case LogLevel.Trace:
               return (syS.ConsoleColor.Blue, "TRC");
            case LogLevel.Debug:
               return (syS.ConsoleColor.Blue, "DBG");
            case LogLevel.Information:
               return (syS.ConsoleColor.White, "INF");
            case LogLevel.Warning:
               return (syS.ConsoleColor.DarkYellow, "WRN");
            case LogLevel.Error:
               return (syS.ConsoleColor.Red, "ERR");
            case LogLevel.Critical:
               return (syS.ConsoleColor.DarkRed, "CRT");
            default:
               return (syS.ConsoleColor.Cyan, "UNK");

         }
      }
      public (syS.ConsoleColor color, string message) GetLogEntryColor(string message)
      {
         var color = syS.ConsoleColor.White;
         if (message.Contains("**COLOR:"))
         {
            var colorString = message.Split("**COLOR:")[1];
            if (Enum.TryParse(colorString, out color))
            {
               return (color, message.Split("**COLOR:")[0]);
            }
         }
         return (color, message);
      }

   }
   public static class ILoggerExtensions
   {
      public static void LogInformation(this ILogger logger, string message, syS.ConsoleColor color)
      {
         logger.LogInformation(FormatMessage(message, color));
      }

      public static void LogDebug(this ILogger logger, string message, syS.ConsoleColor color)
      {
         logger.LogDebug(FormatMessage(message, color));
      }

      public static void LogError(this ILogger logger, string message, syS.ConsoleColor color)
      {
         logger.LogError(FormatMessage(message, color));
      }

      public static void LogWarning(this ILogger logger, string message, syS.ConsoleColor color)
      {
         logger.LogWarning(FormatMessage(message, color));
      }

      public static void LogCritical(this ILogger logger, string message, syS.ConsoleColor color)
      {
         logger.LogCritical(FormatMessage(message, color));
      }

      public static void LogTrace(this ILogger logger, string message, syS.ConsoleColor color)
      {
         logger.LogTrace(FormatMessage(message,color));
      }

      public static void LogInformation(this ILogger logger, Dictionary<string,syS.ConsoleColor> messages)
      {
         logger.LogInformation(FormatMessages(messages));
      }

      public static void LogDebug(this ILogger logger, Dictionary<string, syS.ConsoleColor> messages)
      {
         logger.LogDebug(FormatMessages(messages));
      }

      public static void LogError(this ILogger logger, Dictionary<string, syS.ConsoleColor> messages)
      {
         logger.LogError(FormatMessages(messages));
      }

      public static void LogWarning(this ILogger logger, Dictionary<string, syS.ConsoleColor> messages)
      {
         logger.LogWarning(FormatMessages(messages));
      }

      public static void LogCritical(this ILogger logger, Dictionary<string, syS.ConsoleColor> messages)
      {
         logger.LogCritical(FormatMessages(messages));
      }

      public static void LogTrace(this ILogger logger, Dictionary<string, syS.ConsoleColor> messages)
      {
         logger.LogTrace(FormatMessages(messages));
      }
      private static string FormatMessages(Dictionary<string, syS.ConsoleColor> messages)
      {
         var formattedMessages = string.Empty;
         foreach (var message in messages)
         {
            formattedMessages += $"{message.Key} **COLOR:{message.Value.ToString()}|";
         }
         return formattedMessages;
      }
      private static string FormatMessage(string message, syS.ConsoleColor color)
      {
         return message + " **COLOR:" + color.ToString();
      }

   }
}