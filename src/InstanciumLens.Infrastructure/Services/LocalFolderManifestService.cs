using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace InstanciumLens.Infrastructure.Services
{
    public class LocalFolderManifestService : IFolderManifestService
    {
        public async Task SaveProfileAsync(string folderPath, GraphProfile profile, string ontologySchemaJson)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            string instanciumPath = Path.Combine(folderPath, ".instancium");
            if (!Directory.Exists(instanciumPath))
            {
                var dir = Directory.CreateDirectory(instanciumPath);
                dir.Attributes |= FileAttributes.Hidden;
            }

            // 1. Save the JSON schema separately so the AI can read it during Pass 2
            string schemaPath = Path.Combine(instanciumPath, $"{profile.Id}_schema.json");
            await File.WriteAllTextAsync(schemaPath, ontologySchemaJson);

            // 2. Update the manifest.json for the UI
            string manifestPath = Path.Combine(instanciumPath, "manifest.json");
            FolderManifest manifest;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };

            if (File.Exists(manifestPath))
            {
                string json = await File.ReadAllTextAsync(manifestPath);
                manifest = JsonSerializer.Deserialize<FolderManifest>(json, options) ?? new FolderManifest();
            }
            else
            {
                manifest = new FolderManifest { FolderName = Path.GetFileName(folderPath) ?? "Target Folder" };
            }

            // Ensure no duplicate profile IDs
            manifest.Profiles.RemoveAll(p => p.Id == profile.Id);
            manifest.Profiles.Add(profile);

            string updatedJson = JsonSerializer.Serialize(manifest, options);
            await File.WriteAllTextAsync(manifestPath, updatedJson);
        }
    }
}
