using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Models
{
    public class FolderStatistics
    {
        public int SubfolderCount { get; set; }
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }

        // Helper to format bytes into readable MB/KB
        public string FormattedSize => TotalSizeBytes > 1048576
            ? $"{TotalSizeBytes / 1048576} MB"
            : $"{TotalSizeBytes / 1024} KB";
    }
}
