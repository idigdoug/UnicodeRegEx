namespace UnicodeRegEx.Tests.Tools
{
    using System.Collections.Generic;
    using UnicodeRegEx.Tools.Engine;

    /// <summary>
    /// Records everything a <see cref="SearchJob"/> reports through <see cref="ISearchSink"/>. The job
    /// serializes its callbacks, and a test reads these lists only after awaiting the run, so no extra
    /// synchronization is needed here.
    /// </summary>
    internal sealed class RecordingSink : ISearchSink
    {
        public List<SearchHit> Hits { get; } = new List<SearchHit>();

        public List<string> ChangedFiles { get; } = new List<string>();

        public List<(string Path, string Message)> Errors { get; } = new List<(string, string)>();

        public void OnHit(in SearchHit hit) => Hits.Add(hit);

        public void OnFileChanged(string path) => ChangedFiles.Add(path);

        public void OnError(string path, string message) => Errors.Add((path, message));

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
}
