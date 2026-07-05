namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using UnicodeRegEx.Tools.Engine;

    /// <summary>
    /// Records everything a <see cref="SearchJob"/> reports through <see cref="ISearchSink"/>. The job
    /// serializes its callbacks under a single lock (never invoking two concurrently, even when files are
    /// processed in parallel), and a test reads these lists only after awaiting the run, so no extra
    /// synchronization is needed here. A <see cref="SearchHit"/> is a ref struct valid only during the
    /// callback, so <see cref="OnHit"/> copies out what tests need into a <see cref="RecordedHit"/>.
    /// </summary>
    internal sealed class RecordingSink : ISearchSink
    {
        public List<SearchFile> Files { get; } = new List<SearchFile>();

        public List<RecordedHit> Hits { get; } = new List<RecordedHit>();

        public List<SearchFile> CompletedFiles { get; } = new List<SearchFile>();

        public List<string> ChangedFiles { get; } = new List<string>();

        public List<(string Path, Exception Exception)> Errors { get; } = new List<(string, Exception)>();

        public SearchResponse OnFile(SearchFile file)
        {
            Files.Add(file);
            return SearchResponse.Continue;
        }

        public SearchResponse OnHit(in SearchHit hit)
        {
            Hits.Add(new RecordedHit(hit.File, hit.Text, hit.Replacement));
            return SearchResponse.Continue;
        }

        public void OnFileComplete(SearchFile file) => CompletedFiles.Add(file);

        public void OnFileChanged(string path) => ChangedFiles.Add(path);

        public void OnError(string path, Exception exception) => Errors.Add((path, exception));

        /// <summary>The matched text of every hit, in order.</summary>
        public List<string> HitTexts()
        {
            var texts = new List<string>(Hits.Count);
            foreach (var hit in Hits)
            {
                texts.Add(hit.Text);
            }

            return texts;
        }
    }

    /// <summary>A copy of a <see cref="SearchHit"/>'s values, taken during the callback so it can be
    /// stored and asserted on after the run (the hit itself is a ref struct and cannot be kept).</summary>
    internal sealed class RecordedHit
    {
        public RecordedHit(SearchFile file, string text, string? replacement)
        {
            File = file;
            Text = text;
            Replacement = replacement;
        }

        public SearchFile File { get; }

        public string Text { get; }

        public string? Replacement { get; }
    }
}
