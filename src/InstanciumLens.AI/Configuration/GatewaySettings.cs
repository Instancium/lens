namespace InstanciumLens.AI.Configuration
{
    /// <summary>Plain gateway endpoint configuration; persisted values are injected by the host layer.</summary>
    public sealed class GatewaySettings
    {
        /// <summary>Gateway base URL without hub path (e.g. http://localhost:5000).</summary>
        public string BaseUrl { get; set; } = "http://localhost:5000";

        /// <summary>Bearer key; empty means anonymous negotiate which the gateway rejects for hub methods.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Active model repoId; empty string binds sessions to the currently active model.</summary>
        public string ActiveRepoId { get; set; } = string.Empty;

        /// <summary>Fully qualified SignalR endpoint per wire contract section 3.</summary>
        public string HubUrl => BaseUrl.TrimEnd('/') + "/hub/gateway";
    }
}