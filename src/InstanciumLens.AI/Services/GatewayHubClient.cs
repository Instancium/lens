// src/InstanciumLens.AI/Services/GatewayHubClient.cs
using InstanciumLens.AI.Abstractions;
using InstanciumLens.AI.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace InstanciumLens.AI.Services
{
    /// <summary>
    /// SignalR-based gateway client: session joins, streaming completion deltas and
    /// telemetry forwarding. Connection lifecycle is delegated to GatewayConnectionService.
    /// </summary>
    public sealed class GatewayHubClient : IGatewayHubClient
    {
        private readonly HubConnection _connection;
        private readonly GatewayConnectionService _connectionService;
        private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();

        /// <inheritdoc />
        public event Action<GatewayStatusDetails>? GatewayStatusReceived;

        /// <inheritdoc />
        public event Action<ModelDownloadProgress>? DownloadProgressReceived;

        /// <inheritdoc />
        public event Action<string>? ErrorReceived;

        /// <inheritdoc />
        public event Action<ContextOverflowInfo>? ContextOverflowReceived;

        /// <inheritdoc />
        public event Action<string>? SessionClosedReceived;

        /// <inheritdoc />
        public event Action<int>? QueuePositionReceived;

        /// <inheritdoc />
        public GatewayStatusDetails? LastStatus => _connectionService.LastStatus;

        /// <summary>Initializes the client and wires hub callbacks plus service telemetry.</summary>
        public GatewayHubClient(HubConnection connection, GatewayConnectionService connectionService)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));

            _connection.On<SessionTokenDelta>("ReceiveTokenDelta", HandleTokenDelta);
            _connection.On<string>("ReceiveError", HandleError);
            _connection.On<ContextOverflowInfo>("ReceiveContextOverflow", HandleContextOverflow);
            _connection.On<ModelDownloadProgress>("ReceiveDownloadProgress", progress =>
                DownloadProgressReceived?.Invoke(progress));
            _connection.On<string>("ReceiveSessionClosed", sessionId =>
                SessionClosedReceived?.Invoke(sessionId));
            _connection.On<int>("ReceiveQueuePosition", position =>
                QueuePositionReceived?.Invoke(position));

            _connectionService.StatusReceived += status => GatewayStatusReceived?.Invoke(status);
            _connectionService.ErrorReceived += message => ErrorReceived?.Invoke(message);
        }

        /// <inheritdoc />
        public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
        {
            await _connectionService.ConnectAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<string> RunCompletionAsync(
            string sessionId,
            string repoId,
            IReadOnlyList<GatewayChatMessage> messages,
            bool ephemeral,
            CancellationToken cancellationToken = default)
        {
            ValidateArguments(sessionId, messages);
            PendingRequest pending = new() { TotalTurns = messages.Count };
            await RunCoreAsync(sessionId, repoId, messages, ephemeral, pending, cancellationToken);
            return await pending.Completion.Task;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> RunCompletionStreamAsync(
            string sessionId,
            string repoId,
            IReadOnlyList<GatewayChatMessage> messages,
            bool ephemeral,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ValidateArguments(sessionId, messages);
            Channel<string> channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });
            PendingRequest pending = new()
            {
                Stream = channel,
                TotalTurns = messages.Count,
            };
            _ = RunCoreAsync(sessionId, repoId, messages, ephemeral, pending, cancellationToken);
            await foreach (string token in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return token;
            }
        }

        /// <inheritdoc />
        public async Task DestroySessionAsync(string sessionId)
        {
            try
            {
                await _connection.InvokeAsync("DestroySession", sessionId, CancellationToken.None);
            }
            catch
            {
                // Session may already be evicted on the gateway side.
            }
        }

        /// <inheritdoc />
        public Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync<int>("GetSessionTokenCount", sessionId, cancellationToken);

        /// <inheritdoc />
        public Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync("RollbackSession", sessionId, targetPosition, cancellationToken);

        /// <inheritdoc />
        public Task SubscribeToModelDownloadAsync(string repoId, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync("SubscribeToModelDownload", repoId, cancellationToken);

        /// <inheritdoc />
        public Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken cancellationToken = default) =>
            _connection.InvokeAsync("UnsubscribeFromModelDownload", repoId, cancellationToken);

        /// <inheritdoc />
        public Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default) =>
            _connectionService.FetchStatusAsync(cancellationToken);

        private static void ValidateArguments(string sessionId, IReadOnlyList<GatewayChatMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentNullException(nameof(sessionId));
            }

            if (messages == null || messages.Count == 0)
            {
                throw new ArgumentException("Message sequence must not be empty.", nameof(messages));
            }
        }

        private async Task RunCoreAsync(
              string sessionId,
              string repoId,
              IReadOnlyList<GatewayChatMessage> messages,
              bool ephemeral,
              PendingRequest pending,
              CancellationToken cancellationToken)
        {
            _pending[sessionId] = pending;
            try
            {
                await EnsureConnectedAsync(cancellationToken);
                await JoinAsync(sessionId, repoId, ephemeral, cancellationToken);
                int attempt = 0;
                while (true)
                {
                    try
                    {
                        foreach (GatewayChatMessage message in messages)
                        {
                            await _connection.InvokeAsync(
                                "SendPromptDelta", sessionId, message, cancellationToken);
                        }
                        await pending.Completion.Task.WaitAsync(cancellationToken);
                        return;
                    }
                    catch (Exception ex) when (attempt == 0 && IsSessionEvicted(ex))
                    {
                        attempt++;
                        PendingRequest retried = new()
                        {
                            Stream = pending.Stream,
                            TotalTurns = pending.TotalTurns,
                        };
                        pending = retried;
                        _pending[sessionId] = retried;
                        await JoinAsync(sessionId, repoId, ephemeral, cancellationToken);
                    }
                }
            }
            finally
            {
                _pending.TryRemove(sessionId, out _);
                pending.Stream?.Writer.TryComplete(null);
                await DestroySessionAsync(sessionId);
            }
        }

        private async Task JoinAsync(string sessionId, string repoId, bool ephemeral, CancellationToken cancellationToken)
        {
            await _connection.InvokeAsync("JoinSession", sessionId, repoId ?? string.Empty, cancellationToken);
            if (ephemeral)
            {
                await _connection.InvokeAsync("MarkSessionAsEphemeral", sessionId, cancellationToken);
            }
        }

        // Each SendPromptDelta is a separate inference turn with its own IsDone marker;
        // only the final turn's tokens belong to the caller, earlier turns are discarded.
        private void HandleTokenDelta(SessionTokenDelta delta)
        {
            if (delta == null || !_pending.TryGetValue(delta.SessionId, out PendingRequest? pending))
            {
                return;
            }

            bool isLastTurn = pending.CompletedTurns == pending.TotalTurns - 1;
            if (delta.IsDone)
            {
                if (isLastTurn)
                {
                    _pending.TryRemove(delta.SessionId, out _);
                    AppendToPending(pending, delta.Content);
                    pending.Stream?.Writer.TryComplete(null);
                    pending.Completion.TrySetResult(pending.Buffer.ToString());
                }
                else
                {
                    pending.CompletedTurns++;
                    pending.Buffer.Clear();
                }
                return;
            }

            if (isLastTurn)
            {
                AppendToPending(pending, delta.Content);
            }
        }

        private static void AppendToPending(PendingRequest pending, string content)
        {
            if (pending.Stream != null)
            {
                pending.Stream.Writer.TryWrite(content);
            }
            else
            {
                pending.Buffer.Append(content);
            }
        }


        private void HandleError(string message)
        {
            ErrorReceived?.Invoke(message);
            string? target = _pending.Keys.FirstOrDefault(id => message.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                FailPending(target, BuildErrorException(message));
                return;
            }

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
            if (_pending.TryRemove(sessionId, out PendingRequest? pending))
            {
                pending.Completion.TrySetException(exception);
                pending.Stream?.Writer.TryComplete(exception);
            }
        }

        private void FailAllPending(Exception exception)
        {
            foreach (string key in _pending.Keys.ToArray())
            {
                FailPending(key, exception);
            }
        }

        private sealed class PendingRequest
        {
            public StringBuilder Buffer { get; } = new();
            public Channel<string>? Stream { get; init; }
            // Each SendPromptDelta is a separate inference turn with its own IsDone marker;
            // only the final turn's tokens belong to the caller, earlier turns are discarded.
            public int TotalTurns { get; set; } = 1;
            public int CompletedTurns { get; set; }
            public TaskCompletionSource<string> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}