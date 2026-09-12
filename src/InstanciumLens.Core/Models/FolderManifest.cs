using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Models
{
    public class FolderManifest
    {
        public string FolderName { get; set; } = string.Empty;
        public List<GraphProfile> Profiles { get; set; } = new();
    }
}
