namespace UnicodeRegEx.Cli
{
    using System;
    using System.Threading.Tasks;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Engine;
    using UnicodeRegEx.Tools.Settings;

    internal static class Program
    {
        private const string Usage =
@"usage: unirex [options] <pattern> [path...]
Search files (default) or preview/apply replacements with a Unicode-aware regex.";

        private const string SuggestHelp = "Run 'unirex --help' for detailed usage.";

        private static async Task<int> Main(string[] args)
        {
            try
            {
                var errors = new System.Collections.Generic.List<string>();

                // Settings layer in increasing precedence: built-in defaults < app config < command line.
                var settings = new SearchSettings();
                AppConfigSource.Apply(settings, errors);
                var commandLineParse = CommandLine.Parse(args, settings, errors);

                if (commandLineParse.HelpRequested)
                {
                    Console.Out.WriteLine(HelpFormatter.Format(Usage, settings));
                    return 0;
                }

                if (errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        Console.Error.WriteLine($"error: {error}");
                    }

                    Console.Error.WriteLine(SuggestHelp);
                    return 2;
                }

                var request = new SearchRequest();
                request.ApplySettings(settings);
                request.ApplyPositionals(commandLineParse.Positionals);

                if (request.Paths.Count == 0)
                {
                    request.Paths.Add(".");
                }

                var problems = request.Validate();
                if (problems.Count > 0)
                {
                    foreach (var problem in problems)
                    {
                        Console.Error.WriteLine($"error: {request.DescribeProblemForCommandLine(problem)}");
                    }

                    return 2;
                }

                using var job = new SearchJob(request, new ConsoleSink());
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    job.Cancel();
                };

                await job.RunAsync();

                var summary = job.Summary;
                if (summary.Errors > 0)
                {
                    return 2;
                }

                return summary.AnyMatch ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        /// <summary>Writes engine results and status to the console in grep-like form.</summary>
        private sealed class ConsoleSink : ISearchSink
        {
            // The CLI streams hits and does not surface per-file metadata, so OnFile is a no-op.
            public SearchResponse OnFile(SearchFile file) => SearchResponse.Continue;

            // The CLI streams hits as they arrive (default serial processing keeps them ordered), so it
            // has no per-file buffer to flush.
            public void OnFileComplete(SearchFile file)
            {
            }

            public SearchResponse OnHit(in SearchHit hit)
            {
                Console.Out.WriteLine(hit.Replacement == null
                    ? $"{hit.File.Path}: {hit.Text}"
                    : $"{hit.File.Path}: {hit.Text} => {hit.Replacement}");
                return SearchResponse.Continue;
            }

            public void OnFileChanged(string path)
            {
                Console.Out.WriteLine($"{path}: updated");
            }

            public void OnError(string path, Exception exception)
            {
                // Present missing paths in grep's idiom; otherwise the exception's own message.
                var message = exception is System.IO.FileNotFoundException || exception is System.IO.DirectoryNotFoundException
                    ? "no such file or directory"
                    : exception.Message;
                Console.Error.WriteLine($"{path}: {message}");
            }
        }
    }
}
