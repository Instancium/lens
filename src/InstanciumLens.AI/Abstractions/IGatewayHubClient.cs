using InstanciumLens.AI.Contracts;

namespace InstanciumLens.AI.Abstractions
{
    /// <summary>SignalR client facade over the InstantAIGate GatewayHub wire contract.</summary>
    public interface IGatewayHubClient
    {
        /// <summary>Fired on every ReceiveGatewayStatus snapshot.</summary>
        event Action<GatewayStatusDetails>? GatewayStatusReceived;

        /// <summary>Fired on ReceiveDownloadProgress for subscribed model repos.</summary>
        event Action<ModelDownloadProgress>? DownloadProgressReceived;

        /// <summary>Fired on ReceiveError business errors.</summary>
        event Action<string>? ErrorReceived;

        /// <summary>Fired on ReceiveContextOverflow structured payloads.</summary>
        event Action<ContextOverflowInfo>? ContextOverflowReceived;

        /// <summary>Fired on ReceiveSessionClosed confirmations.</summary>
        event Action<string>? SessionClosedReceived;

        /// <summary>Fired on ReceiveQueuePosition when inference is queued.</summary>
        event Action<int>? QueuePositionReceived;

        /// <summary>Last received gateway status snapshot, if any.</summary>
        GatewayStatusDetails? LastStatus { get; }

        /// <summary>Opens the underlying connection if it is not already open.</summary>
        Task EnsureConnectedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Full request/completion cycle: Join, optional ephemeral mark, sequential SendPromptDelta,
        /// stream collection until IsDone, single retry with re-Join on session eviction,
        /// best-effort DestroySession in all exit paths.
        /// </summary>
        /// <returns>Accumulated assistant content of the final prompt in the sequence.</returns>
        Task<string> RunCompletionAsync(
            string sessionId,
            string repoId,
            IReadOnlyList<GatewayChatMessage> messages,
            bool ephemeral,
            CancellationToken cancellationToken = default);

        /// <summary>Hard-terminates a session and frees its VRAM KV-slot.</summary>
        Task DestroySessionAsync(string sessionId);

        /// <summary>Returns current KV-cache length in tokens (checkpoint baseline).</summary>
        Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>Truncates KV-cache up to the given token position.</summary>
        Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken cancellationToken = default);

        /// <summary>Subscribes the connection to a model download progress group.</summary>
        Task SubscribeToModelDownloadAsync(string repoId, CancellationToken cancellationToken = default);

        /// <summary>Unsubscribes the connection from a model download progress group.</summary>
        Task UnsubscribeFromModelDownloadAsync(string repoId, CancellationToken cancellationToken = default);

        /// <summary>Requests an instant gateway status snapshot.</summary>
        Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default);
    }
}