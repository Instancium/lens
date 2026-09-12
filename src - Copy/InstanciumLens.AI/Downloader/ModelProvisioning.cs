using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace InstanciumLens.AI.Downloader
{
    public class ModelProvisioning
    {
        private readonly HttpClient _httpClient;
        private Task? _downloadTask;
        private readonly object _lock = new();

        // Global state properties that survive page reloads
        public double Percent { get; private set; }
        public bool IsComplete { get; private set; }

        // Event to notify the Blazor UI when progress changes
        public event Action? OnProgressChanged;

        public bool IsModelReady { get; private set; }

        public ModelProvisioning(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Quickly checks the file system to determine if the model is already available.
        /// Useful for UI components (like navigation menus) to display real-time status.
        /// </summary>
        public void EvaluateModelStatus(string modelFileName, long expectedSizeBytes)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destinationPath = Path.Combine(appDataPath, "InstanciumLens", "Models", modelFileName);

            // FIX: If the final file exists without the .part extension, it is 100% complete.
            // We no longer rely on exact byte matching, which causes false-negative wipes.
            if (File.Exists(destinationPath))
            {
                IsModelReady = true;
                IsComplete = true;
                Percent = 100.0;
                OnProgressChanged?.Invoke();
                return;
            }

            IsModelReady = false;
            OnProgressChanged?.Invoke();
        }

        public void StartDownloadIfNotRunning(string downloadUrl, string modelFileName, long expectedSizeBytes)
        {
            lock (_lock)
            {
                // Prevent starting a new download if one is already active in the background
                if (_downloadTask != null && !_downloadTask.IsCompleted)
                    return;

                if (IsComplete)
                    return;

                // Start the background process (Fire and forget)
                _downloadTask = ExecuteDownloadAsync(downloadUrl, modelFileName, expectedSizeBytes);
            }
        }

        private async Task ExecuteDownloadAsync(string downloadUrl, string modelFileName, long expectedSizeBytes)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destinationFolder = Path.Combine(appDataPath, "InstanciumLens", "Models");

            string finalDestinationPath = Path.Combine(destinationFolder, modelFileName);

            // FIX: Download into a temporary .part file first
            string tempDownloadPath = finalDestinationPath + ".part";

            if (!Directory.Exists(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            // Double-check before starting
            if (File.Exists(finalDestinationPath))
            {
                Percent = 100.0;
                IsComplete = true;
                IsModelReady = true;
                OnProgressChanged?.Invoke();
                return;
            }

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? expectedSizeBytes;
            using var downloadStream = await response.Content.ReadAsStreamAsync();

            // Write to the temporary .part file
            using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true))
            {
                var buffer = new byte[256 * 1024];
                long totalRead = 0;
                int read;

                while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    Percent = totalBytes > 0 ? ((double)totalRead / totalBytes) * 100.0 : 0.0;
                    OnProgressChanged?.Invoke();
                }
            } // fileStream is safely closed here

            // FIX: Atomic rename. Once the file is completely downloaded, remove the .part extension
            File.Move(tempDownloadPath, finalDestinationPath, true);

            Percent = 100.0;
            IsComplete = true;
            IsModelReady = true;
            OnProgressChanged?.Invoke();
        }
    }
}