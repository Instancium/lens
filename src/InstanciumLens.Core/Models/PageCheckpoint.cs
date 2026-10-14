using System;
using System.Collections.Generic;

namespace InstanciumLens.Core.Models
{
    /// <summary>
    /// Per-page pipeline checkpoint row that allows Pass 1 and Pass 2 to resume after an application restart.
    /// </summary>
    public sealed class PageCheckpoint
    {
        public string PageKey { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public string FileFingerprint { get; set; } = string.Empty;
        public string SchemaHash { get; set; } = string.Empty;
        public bool Pass1Completed { get; set; }
        public bool Pass2Completed { get; set; }
        public string? RawText { get; set; }
    }

    /// <summary>
    /// Snapshot of all page checkpoints of one profile graph taken at pipeline start.
    /// </summary>
    public sealed class PipelineCheckpointSnapshot
    {
        private readonly Dictionary<string, PageCheckpoint> _pages = new(StringComparer.Ordinal);

        public IReadOnlyCollection<PageCheckpoint> Pages => _pages.Values;

        public void Add(PageCheckpoint page) => _pages[page.PageKey] = page;

        public bool TryGet(string pageKey, out PageCheckpoint? page) =>
            _pages.TryGetValue(pageKey, out page);
    }
}