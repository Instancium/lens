// src/InstanciumLens.AI/Services/GatewaySessionManager.cs
using InstanciumLens.AI.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace InstanciumLens.AI.Services
{
    /// <summary>
    /// Backward-compatible facade over GatewayConnectionService for pages written against
    /// the pre-refactor session manager API. Keeps old injection points compiling.
    /// </summary>
    public sealed class GatewaySessionManager
    {
        private readonly GatewayConnectionService _connectionService;

        /// <summary>Raised on any connection/state/telemetry change.</summary>
        public event Action? OnStatusChanged;

        /// <summary>Wraps the connection service and forwards its state changes.</summary>
        public GatewaySessionManager(GatewayConnectionService connectionService)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _connectionService.StateChanged += () => OnStatusChanged?.Invoke();
        }

        /// <summary>Current SignalR connection state.</summary>
        public HubConnectionState State => _connectionService.ConnectionState;

        /// <summary>True when the hub connection is established.</summary>
        public bool IsConnected => _connectionService.ConnectionState == HubConnectionState.Connected;

        /// <summary>Latest telemetry snapshot, if any.</summary>
        public GatewayStatusDetails? LastStatus => _connectionService.LastStatus;

        /// <summary>Reason of the last connection failure, if any.</summary>
        public string? LastError => _connectionService.LastError;

        /// <summary>Starts connection with background retry. Safe to call multiple times.</summary>
        public Task InitializeAsync() => _connectionService.StartAsync();
    }
}