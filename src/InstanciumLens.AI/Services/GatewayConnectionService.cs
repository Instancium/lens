// src/InstanciumLens.AI/Services/GatewayConnectionService.cs
using InstanciumLens.AI.Configuration;
using InstanciumLens.AI.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace InstanciumLens.AI.Services
{
    /// <summary>
    /// Owns the gateway SignalR connection lifecycle: initial connect, background retry with
    /// backoff and telemetry aggregation for UI layers. Single source of truth for connection state.
    /// </summary>
    public sealed class GatewayConnectionService
    {
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        private readonly HubConnection _connection;
        private readonly GatewaySettings _settings;
        private readonly ILogger<GatewayConnectionService> _logger;
        private readonly SemaphoreSlim _startLock = new(1, 1);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly object _loopGuard = new();
        private Task? _retryLoop;

        /// <summary>Raised whenever status, connection state or last error changes.</summary>
        public event Action? StateChanged;

        /// <summary>Raised when the gateway pushes an operational status snapshot.</summary>
        public event Action<GatewayStatusDetails>? StatusReceived;

        /// <summary>Raised when the gateway pushes a free-form error message.</summary>
        public event Action<string>? ErrorReceived;

        /// <summary>Latest telemetry snapshot received from the gateway, if any.</summary>
        public GatewayStatusDetails? LastStatus { get; private set; }

        /// <summary>Human-readable reason of the last connection failure.</summary>
        public string? LastError { get; private set; }

        /// <summary>Current SignalR connection state.</summary>
        public HubConnectionState ConnectionState => _connection.State;

        /// <summary>Initializes the service and subscribes to hub callbacks.</summary>
        public GatewayConnectionService(
            HubConnection connection,
            GatewaySettings settings,
            ILogger<GatewayConnectionService> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", status =>
            {
                LastStatus = status;
                LastError = null;
                StatusReceived?.Invoke(status);
                RaiseStateChanged();
            });

            _connection.On<string>("ReceiveError", message =>
            {
                _logger.LogWarning("Gateway error event: {Message}", message);
                ErrorReceived?.Invoke(message);
            });

            _connection.Reconnecting += error =>
            {
                _logger.LogWarning(error, "Gateway connection is reconnecting");
                LastError = error?.Message ?? "Reconnecting...";
                RaiseStateChanged();
                return Task.CompletedTask;
            };

            _connection.Reconnected += connectionId =>
            {
                _logger.LogInformation("Gateway reconnected, connection id {ConnectionId}", connectionId);
                LastError = null;
                RaiseStateChanged();
                return Task.CompletedTask;
            };

            _connection.Closed += error =>
            {
                _logger.LogWarning(error, "Gateway connection closed");
                LastError = error?.Message ?? "Connection closed by gateway.";
                RaiseStateChanged();
                EnsureRetryLoopRunning();
                return Task.CompletedTask;
            };
        }

        /// <summary>Starts the background connect/retry loop. Safe to call multiple times.</summary>
        public Task StartAsync()
        {
            EnsureRetryLoopRunning();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs a single connection attempt if the hub is disconnected.
        /// Throws on failure and schedules background retry; used before hub invocations.
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _startLock.WaitAsync(cancellationToken);
            try
            {
                if (_connection.State == HubConnectionState.Disconnected)
                {
                    try
                    {
                        _logger.LogInformation("Connecting to gateway hub {HubUrl}", _settings.HubUrl);
                        await _connection.StartAsync(cancellationToken);
                        LastError = null;
                        RaiseStateChanged();
                    }
                    catch (Exception ex)
                    {
                        LastError = BuildUserFacingError(ex);
                        _logger.LogError(ex, "Gateway connection attempt failed");
                        RaiseStateChanged();
                        EnsureRetryLoopRunning();
                        throw;
                    }
                }
            }
            finally
            {
                _startLock.Release();
            }
        }

        /// <summary>Requests a fresh telemetry snapshot from the gateway.</summary>
        public async Task<GatewayStatusDetails> FetchStatusAsync(CancellationToken cancellationToken = default)
        {
            GatewayStatusDetails status = await _connection.InvokeAsync<GatewayStatusDetails>(
                "GetGatewayStatus",
                cancellationToken);
            LastStatus = status;
            LastError = null;
            RaiseStateChanged();
            return status;
        }

        /// <summary>Stops the retry loop. The shared HubConnection is owned by the DI container.</summary>
        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            Task? loop = _retryLoop;
            if (loop != null)
            {
                try
                {
                    await loop;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown.
                }
            }

            _startLock.Dispose();
            _shutdown.Dispose();
        }

        private void EnsureRetryLoopRunning()
        {
            lock (_loopGuard)
            {
                if (_shutdown.IsCancellationRequested)
                {
                    return;
                }

                if (_retryLoop == null || _retryLoop.IsCompleted)
                {
                    _retryLoop = Task.Run(() => RetryLoopAsync(_shutdown.Token));
                }
            }
        }

        private async Task RetryLoopAsync(CancellationToken cancellationToken)
        {
            int attempt = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ConnectAsync(cancellationToken);

                    try
                    {
                        await FetchStatusAsync(cancellationToken);
                    }
                    catch (Exception statusEx) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(statusEx, "Gateway status fetch failed");
                    }

                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;
                    LastError = BuildUserFacingError(ex);
                    _logger.LogError(ex, "Gateway connection attempt {Attempt} failed", attempt);
                    RaiseStateChanged();

                    TimeSpan delay = attempt < RetryDelays.Length
                        ? RetryDelays[attempt - 1]
                        : RetryDelays[RetryDelays.Length - 1];
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private static string BuildUserFacingError(Exception exception)
        {
            if (exception is HttpRequestException)
            {
                return "Gateway unreachable. Check that InstantAIGate is running.";
            }

            if (exception is InvalidOperationException)
            {
                return "Hub URL is misconfigured. Check Base URL in Settings.";
            }

            return $"Connection failed: {exception.Message}";
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}