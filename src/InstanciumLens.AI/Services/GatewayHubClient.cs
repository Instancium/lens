using InstanciumLens.AI.Abstractions;
using InstanciumLens.AI.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Text;

namespace InstanciumLens.AI.Services
{
    /// <summary>
    /// SignalR implementation of the gateway facade. Owns pending stream completions,
    /// routes server callbacks and enforces the ephemeral-session discipline from the wire contract.
    /// </summary>
    public sealed class GatewayHubClient : IGatewayHubClient
    {
        private readonly HubConnection _connection;
        private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        /// <inheritdoc/>
        public event Action<GatewayStatusDetails>? GatewayStatusReceived;

        /// <inheritdoc/>
        public event Action<ModelDownloadProgress>? DownloadProgressReceived;

        /// <inheritdoc/>
        public event Action<string>? ErrorReceived;

        /// <inheritdoc/>
        public event Action<ContextOverflowInfo>? ContextOverflowReceived;

        /// <inheritdoc/>
        public event Action<string>? SessionClosedReceived;

        /// <inheritdoc/>
        public event Action<int>? QueuePositionReceived;

        /// <inheritdoc/>
        public GatewayStatusDetails? LastStatus { get; private set; }

        /// <summary>Initializes the client and wires all server callbacks.</summary>
        public GatewayHubClient(HubConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));

            _connection.On<SessionTokenDelta>("ReceiveTokenDelta", HandleTokenDelta);
            _connection.On<string>("ReceiveError", HandleError);
            _connection.On<ContextOverflowInfo>("ReceiveContextOverflow", HandleContextOverflow);
            _connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", status =>
            {
                LastStatus = status;
                GatewayStatusReceived?.Invoke(status);
            });
            _connection.On<ModelDownloadProgress>("ReceiveDownloadProgress", progress =>
                DownloadProgressReceived?.Invoke(progress));
            _connection.On<string>("ReceiveSessionClosed", sessionId =>
                SessionClosedReceived?.Invoke(sessionId));
            _connection.On<int>("ReceiveQueuePosition", position =>
                QueuePositionReceived?.Invoke(position));

            // Server evicts every session of the old ConnectionId on drop; pending streams are unrecoverable.
            _connection.Reconnected += _ =>
            {
                FailAllPending(new GatewaySessionInactiveException("Connection reconnected; server evicted all sessions."));
                return Task.CompletedTask;
            };
            _connection.Closed += _ =>
            {
                FailAllPending(new GatewayException("Gateway connection closed."));
                return Task.CompletedTask;
            };
        }

        /// <inheritdoc/>
        public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                return;
            }

            await _connectLock.WaitAsync(cancellationToken);
            try
            {
                if (_connection.State == HubConnectionState.Disconnected)
                {
                    await _connection.StartAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new GatewayException($"Failed to start gateway connection: {ex.Message}", ex);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<string> RunCompletionAsync(
            string sessionId,
            string repoId,
            IReadOnlyList<GatewayChatMessage> messages,
            bool ephemeral,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentNullException(nameof(sessionId));
            if (messages == null || messages.Count == 0) throw new ArgumentException("Message sequence must not be empty.", nameof(messages));

            await EnsureConnectedAsync(cancellationToken);
            await JoinAsync(sessionId, repoId, ephemeral, cancellationToken);

            int attempt = 0;
            while (true)
            {
                var pending = new PendingRequest();
                _pending[sessionId] = pending;
                try
                {
                    foreach (var message in messages)
                    {
                        await _connection.InvokeAsync("SendPromptDelta", sessionId, message, cancellationToken);
                    }

                    return await pending.Completion.Task.WaitAsync(cancellationToken);
                }
                catch (Exception ex) when (attempt == 0 && IsSessionEvicted(ex))
                {
                    // Wire contract 8.2: single retry with re-Join before emitting tokens.
                    attempt++;
                    _pending.TryRemove(sessionId, out _);
                    await JoinAsync(sessionId, repoId, ephemeral, cancellationToken);
                }
                catch
                {
                    _pending.TryRemove(sessionId, out _);
                    throw;
                }
            }
            // DestroySession is intentionally NOT here: JoinAsync of the next attempt must see a clean slate,
            // and the finally-freeing happens in the wrapper below.
        }

        /// <inheritdoc/>
        public async Task DestroySessionAsync(string sessionId)
        {
            try
            {
                await _connection.InvokeAsync("DestroySession", sessionId, CancellationToken.None);
            }
            catch
            {
                // Best-effort by contract: ephemeral declaration is the insurance policy.
            }
        }

        /// <inheritdoc/>
        public Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync<int>("GetSessionTokenCount", sessionId, cancellationToken);

        /// <inheritdoc/>
        public Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync("RollbackSession", sessionId, targetPosition, cancellationToken);

        /// <inheritdoc/>
        public Task SubscribeToModelDownloadAsync(string repoId, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync("SubscribeToModelDownload", repoId, cancellationToken);

        /// <inheritdoc/>
        public Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync("UnsubscribeFromModelDownload", repoId, cancellationToken);

        /// <inheritdoc/>
        public Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus", cancellationToken);

        private async Task JoinAsync(string sessionId, string repoId, bool ephemeral, CancellationToken cancellationToken)
        {
            await _connection.InvokeAsync("JoinSession", sessionId, repoId ?? string.Empty, cancellationToken);
            if (ephemeral)
            {
                await _connection.InvokeAsync("MarkSessionAsEphemeral", sessionId, cancellationToken);
            }
        }

        private void HandleTokenDelta(SessionTokenDelta delta)
        {
            if (delta == null || !_pending.TryGetValue(delta.SessionId, out var pending))
            {
                return;
            }

            pending.Buffer.Append(delta.Content);
            if (delta.IsDone)
            {
                _pending.TryRemove(delta.SessionId, out _);
                pending.Completion.TrySetResult(pending.Buffer.ToString());
            }
        }

        private void HandleError(string message)
        {
            ErrorReceived?.Invoke(message);

            var target = _pending.Keys.FirstOrDefault(id => message.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                FailPending(target, BuildErrorException(message));
                return;
            }

            // Unroutable business error: fail everything pending so callers never hang silently.
            FailAllPending(BuildErrorException(message));
        }

        private void HandleContextOverflow(ContextOverflowInfo info)
        {
            ContextOverflowReceived?.Invoke(info);
            FailPending(info.SessionId, new GatewayContextOverflowException(info));
        }

        private static Exception BuildErrorException(string message) =>
            message.Contains("not active", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("was not found", StringComparison.OrdinalIgnoreCase)
                ? new GatewaySessionInactiveException(message)
                : new GatewayException(message);

        private static bool IsSessionEvicted(Exception ex) =>
            ex is GatewaySessionInactiveException ||
            (ex is HubException hub && (hub.Message.Contains("not active", StringComparison.OrdinalIgnoreCase)
                || hub.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase)));

        private void FailPending(string sessionId, Exception exception)
        {
            if (_pending.TryRemove(sessionId, out var pending))
            {
                pending.Completion.TrySetException(exception);
            }
        }

        private void FailAllPending(Exception exception)
        {
            foreach (var key in _pending.Keys.ToArray())
            {
                FailPending(key, exception);
            }
        }

        private sealed class PendingRequest
        {
            public StringBuilder Buffer { get; } = new();
            public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}