using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.App.Models
{
    public class ProfileItem
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = "📊";
        public DateTime CreatedAt { get; set; }
        public int DocumentCount { get; set; }
    }
}
