using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;

namespace InstanciumLens.AI.Services
{
    /// <summary>
    /// Manages the lifecycle and state of the connection to InstantAIGate.
    /// </summary>
    public class GatewaySessionManager : IAsyncDisposable
    {
        private readonly HubConnection _hubConnection;

        /// <summary>
        /// Triggered when the connection state changes (Connected, Disconnected, Reconnecting).
        /// Initialized with an empty delegate to satisfy strict nullable reference types
        /// and avoid null-checks on invocation.
        /// </summary>
        public event Action OnStatusChanged = delegate { };

        /// <summary>
        /// Gets a value indicating whether the client is currently connected to the gateway.
        /// </summary>
        public bool IsConnected => _hubConnection.State == HubConnectionState.Connected;

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        public HubConnectionState ConnectionState => _hubConnection.State;

        /// <summary>
        /// Initializes a new instance of the <see cref="GatewaySessionManager"/> class.
        /// </summary>
        /// <param name="hubConnection">The injected SignalR HubConnection.</param>
        public GatewaySessionManager(HubConnection hubConnection)
        {
            _hubConnection = hubConnection ?? throw new ArgumentNullException(nameof(hubConnection));

            _hubConnection.Closed += OnConnectionClosed;
            _hubConnection.Reconnecting += OnConnectionReconnecting;
            _hubConnection.Reconnected += OnConnectionReconnected;
        }

        /// <summary>
        /// Initializes the connection to the gateway asynchronously.
        /// Should be called during app startup or layout initialization.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await _hubConnection.StartAsync();
                    OnStatusChanged();
                }
                catch 
                {
                    // Swallowing startup exceptions intentionally: the UI will reflect
                    // the disconnected state via the status change event triggered below.
                    OnStatusChanged();
                }
            }
        }

        private async Task OnConnectionClosed(Exception? error)
        {
            OnStatusChanged();
            // Automatic reconnect logic is handled by WithAutomaticReconnect() in the builder, 
            // but we can add custom logic here if needed.
            await Task.CompletedTask;
        }

        private Task OnConnectionReconnecting(Exception? error)
        {
            OnStatusChanged();
            return Task.CompletedTask;
        }

        private Task OnConnectionReconnected(string? connectionId)
        {
            OnStatusChanged();
            // Server evicts all sessions of the old ConnectionId on drop; 
            // UI must handle re-joining via status change notification.
            return Task.CompletedTask;
        }

        /// <summary>
        /// Disposes the HubConnection and unsubscribes from events.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_hubConnection != null)
            {
                _hubConnection.Closed -= OnConnectionClosed;
                _hubConnection.Reconnecting -= OnConnectionReconnecting;
                _hubConnection.Reconnected -= OnConnectionReconnected;

                await _hubConnection.DisposeAsync();
            }
        }
    }
}