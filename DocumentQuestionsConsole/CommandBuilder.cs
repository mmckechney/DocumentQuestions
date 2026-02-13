using DocumentQuestions.Library;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;

namespace DocumentQuestions.Console
{
   internal class CommandBuilder
   {
      public static Parser BuildCommandLine()
      {

         var docOpt = new Option<string>(new string[] { "--doc", "-d" }, () => "", "Name of the document to inquire about.") { IsRequired = true };
         var docCommand = new Command("doc", "Set the active document to start asking questions");
         var documentArg = new Argument<string[]>("document", "Document to set as active") { Arity = ArgumentArity.ZeroOrMore };
         docCommand.Add(documentArg);
         docCommand.Handler = CommandHandler.Create<string[]>(Worker.SetActiveDocument);

         var questionArg = new Argument<string[]>("question", "Question to ask about the document") { Arity = ArgumentArity.ZeroOrMore };
         var askQuestionCommand = new Command("ask", "Ask a question on the document(s)");
         askQuestionCommand.Add(questionArg);
         askQuestionCommand.Handler = CommandHandler.Create<string[]>(Worker.AskQuestion);

         var fileOpt = new Option<string>(new string[] { "--file", "-f" }, "Path to the file to process and index (surround with quotes if there are spaces in the name)") { IsRequired = true };
         var modelOpt = new Option<string>(new string[] { "--model", "-m" }, () => "prebuilt-layout", $"Model to use for processing the document: {string.Join(", ", DocumentIntelligence.ModelList)}");
         var indexNameOpt = new Option<string>(new string[] { "--index", "-i" }, $"Custom index name, otherwise it will default to the file name");
         var processFileCommand = new Command("process", "Process the file contents against Document Intelligence and add to Azure AI Search index")
         {
            fileOpt,
            modelOpt,
            indexNameOpt
         };
         processFileCommand.Handler = CommandHandler.Create<string, string, string>(Worker.ProcessFile);


         var listCommand = new Command("list", "List the available files to ask questions about");
         listCommand.Handler = CommandHandler.Create(Worker.ListFiles);

         var clearIndexCommand = new Command("clear-index", "Clears the index of all records");
         clearIndexCommand.Handler = CommandHandler.Create(Worker.ClearIndex);

         var askAllArg = new Argument<string[]>("question", "Question to ask across all documents") { Arity = ArgumentArity.ZeroOrMore };
         var askAllCommand = new Command("ask-all", "Ask a question across all indexed documents");
         askAllCommand.Add(askAllArg);
         askAllCommand.Handler = CommandHandler.Create<string[]>(Worker.AskAllDocuments);

         var summarizeCommand = new Command("summarize", "Summarize the active document");
         summarizeCommand.Handler = CommandHandler.Create(Worker.SummarizeDocument);

         RootCommand rootCommand = new RootCommand(description: $"Utility to ask questions on documents that have been indexed in Azure AI Search");
         rootCommand.Add(questionArg);
         rootCommand.Handler = CommandHandler.Create<string[]>(Worker.AskQuestion);
         rootCommand.Add(docCommand);
         rootCommand.Add(askQuestionCommand);
         rootCommand.Add(processFileCommand);
         rootCommand.Add(listCommand);
         rootCommand.Add(clearIndexCommand);
         rootCommand.Add(askAllCommand);
         rootCommand.Add(summarizeCommand);

         var parser = new CommandLineBuilder(rootCommand)
              .UseDefaults()
              .UseHelp(ctx =>
              {
                 ctx.HelpBuilder.CustomizeLayout(_ => HelpBuilder.Default
                                    .GetLayout()
                                    .Prepend(
                                        _ => AnsiConsole.Write(new FigletText("Ask Document Questions"))
                                    ));

              })
              .Build();

         return parser;
      }
   }
}
