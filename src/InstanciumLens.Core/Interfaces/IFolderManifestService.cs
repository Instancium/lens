using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IFolderManifestService
    {
        /// <summary>
        /// Saves a generated graph profile and its associated ontology schema to the hidden directory.
        /// </summary>
        Task SaveProfileAsync(string folderPath, GraphProfile profile, string ontologySchemaJson);
    }
}
