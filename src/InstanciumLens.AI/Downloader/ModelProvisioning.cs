using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace InstanciumLens.AI.Downloader
{
    public class ModelProvisioning
    {
        private readonly HttpClient _httpClient;
        private Task? _downloadTask;
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;

        public double Percent { get; private set; }
        public bool IsComplete { get; private set; }
        public string? ErrorMessage { get; private set; }
        public event Action? OnProgressChanged;
        public bool IsModelReady { get; private set; }

        public ModelProvisioning(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.Timeout = TimeSpan.FromHours(2); 
        }

        public void EvaluateModelStatus(string modelFileName, long expectedSizeBytes)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destinationPath = Path.Combine(appDataPath, "InstanciumLens", "Models", modelFileName);
            string tempDownloadPath = destinationPath + ".part";

            if (File.Exists(destinationPath))
            {
                IsModelReady = true;
                IsComplete = true;
                Percent = 100.0;
                ErrorMessage = null;
                OnProgressChanged?.Invoke();
                return;
            }

            IsModelReady = false;
            IsComplete = false;
            ErrorMessage = null;

            // Check if there is a partial download to show correct initial percentage
            if (File.Exists(tempDownloadPath))
            {
                var fileInfo = new FileInfo(tempDownloadPath);
                Percent = expectedSizeBytes > 0 ? ((double)fileInfo.Length / expectedSizeBytes) * 100.0 : 0.0;
            }
            else
            {
                Percent = 0.0;
            }

            OnProgressChanged?.Invoke();
        }

        public void StartDownloadIfNotRunning(string downloadUrl, string modelFileName, long expectedSizeBytes)
        {
            lock (_lock)
            {
                if (_downloadTask != null && !_downloadTask.IsCompleted && !_downloadTask.IsCanceled && !_downloadTask.IsFaulted)
                    return;

                if (IsComplete)
                    return;

                _cts = new CancellationTokenSource();
                _downloadTask = ExecuteDownloadAsync(downloadUrl, modelFileName, expectedSizeBytes, _cts.Token);
            }
        }

        private async Task ExecuteDownloadAsync(string downloadUrl, string modelFileName, long expectedSizeBytes, CancellationToken cancellationToken)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destinationFolder = Path.Combine(appDataPath, "InstanciumLens", "Models");
            string finalDestinationPath = Path.Combine(destinationFolder, modelFileName);
            string tempDownloadPath = finalDestinationPath + ".part";

            if (!Directory.Exists(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            if (File.Exists(finalDestinationPath))
            {
                Percent = 100.0;
                IsComplete = true;
                IsModelReady = true;
                ErrorMessage = null;
                OnProgressChanged?.Invoke();
                return;
            }

            long startByte = 0;
            if (File.Exists(tempDownloadPath))
            {
                startByte = new FileInfo(tempDownloadPath).Length;
            }

            int maxRetries = 5;
            int retryDelayMs = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    
                    // Add Range header to resume download from the last saved byte
                    if (startByte > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(startByte, null);
                    }

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        // Server does not support resuming, start from scratch
                        File.Delete(tempDownloadPath);
                        startByte = 0;
                        attempt--;
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    long totalBytes = response.Content.Headers.ContentLength ?? (expectedSizeBytes - startByte);
                    using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                    FileMode fileMode = startByte > 0 ? FileMode.Append : FileMode.Create;
                    using (var fileStream = new FileStream(tempDownloadPath, fileMode, FileAccess.Write, FileShare.None, 256 * 1024, true))
                    {
                        var buffer = new byte[256 * 1024];
                        long totalRead = startByte;
                        int read;

                        while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                            totalRead += read;

                            long expectedTotal = expectedSizeBytes > 0 ? expectedSizeBytes : totalRead;
                            Percent = ((double)totalRead / expectedTotal) * 100.0;
                            if (Percent > 100.0) Percent = 100.0;

                            OnProgressChanged?.Invoke();
                        }
                    }

                    // Atomic rename after successful download
                    File.Move(tempDownloadPath, finalDestinationPath, true);
                    Percent = 100.0;
                    IsComplete = true;
                    IsModelReady = true;
                    ErrorMessage = null;
                    OnProgressChanged?.Invoke();
                    return; // Success, exit retry loop
                }
                catch (OperationCanceledException)
                {
                    return; // Download was canceled
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        ErrorMessage = $"Download failed after {maxRetries} attempts: {ex.Message}";
                        OnProgressChanged?.Invoke();
                        return;
                    }
                    // Wait before next attempt (exponential backoff: 2s, 4s, 6s...)
                    await Task.Delay(retryDelayMs * attempt, cancellationToken);
                }
            }
        }
    }
}