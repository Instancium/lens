using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Models
{
    public class GraphProfile
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DatabaseFile { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int DocumentCount { get; set; }
        public string Icon { get; set; } = "📊";
    }
}
