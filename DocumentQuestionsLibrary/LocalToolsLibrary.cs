using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocumentQuestions.Library
{
   public class LocalToolsLibrary
   {
      [Description("Fetches the weather information for the specified location")]
      public string FetchWeather([Description("The location to fetch weather for.")] string location)
      {
         // Mock weather data for demonstration purposes
         var mockWeatherData = new Dictionary<string, string>
            {
                { "New York", "Sunny, 25°C" },
                { "London", "Cloudy, 18°C" },
                { "Tokyo", "Rainy, 22°C" },
                { "Seattle", "Rainy, 10°C" }
            };

         string weather = mockWeatherData.TryGetValue(location, out string? weatherInfo)
             ? weatherInfo
             : "Weather data not available for this location.";

         var result = new { weather = weather };
         return JsonSerializer.Serialize(result);
      }

      [Description("Calculates the sum of two numbers")]
      public string Calculator([Description("First number")] double a, [Description("Second number")] double b, [Description("Operation to perform [add, subtract, multiply, divide]")] string operation = "add")
      {
         double result = operation.ToLower() switch
         {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => b != 0 ? a / b : throw new ArgumentException("Cannot divide by zero"),
            _ => throw new ArgumentException($"Unknown operation: {operation}")
         };

         return JsonSerializer.Serialize(new { operation, a, b, result });
      }

      [Description("Gets the current date and time")]
      public string GetCurrentDateTime([Description("Format for the date/time")] string format = "yyyy-MM-dd HH:mm:ss")
      {
         try
         {
            var now = DateTime.Now;
            return JsonSerializer.Serialize(new
            {
               formatted = now.ToString(format),
               utc = now.ToUniversalTime().ToString("o"),
               timestamp = ((DateTimeOffset)now).ToUnixTimeSeconds()
            });
         }
         catch (Exception ex)
         {
            return JsonSerializer.Serialize(new { error = ex.Message, defaultFormat = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
         }
      }
   }
}
