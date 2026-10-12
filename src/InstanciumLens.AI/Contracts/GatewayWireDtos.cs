using System.Text.Json.Serialization;

namespace InstanciumLens.AI.Contracts
{
    /// <summary>Gateway operational state codes (wire contract section 6).</summary>
    public enum GatewayOperationalStatus
    {
        Uninitialized = 0,
        ModelDownloading = 1,
        ModelLoading = 2,
        Ready = 3,
        Faulted = 4
    }

    /// <summary>Mirror of GatewayStatusDetails telemetry snapshot.</summary>
    public sealed class GatewayStatusDetails
    {
        public GatewayOperationalStatus Status { get; set; }
        public string? ActiveModelId { get; set; }
        public string? StageDescription { get; set; }
        public double ProgressPercentage { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>Mirror of SessionTokenDelta streaming payload.</summary>
    public sealed class SessionTokenDelta
    {
        public string SessionId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsDone { get; set; }
        public string? FinishReason { get; set; }
    }

    /// <summary>Mirror of DownloadProgress broadcast payload.</summary>
    public sealed class ModelDownloadProgress
    {
        public string ModelId { get; set; } = string.Empty;
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedBytesPerSecond { get; set; }
        public double Percentage { get; set; }
    }

    /// <summary>Mirror of structured context overflow payload.</summary>
    public sealed class ContextOverflowInfo
    {
        public string SessionId { get; set; } = string.Empty;
        public int PastTokens { get; set; }
        public int IncomingTokens { get; set; }
        public int ReservedTokens { get; set; }
        public int ContextSize { get; set; }
    }

    /// <summary>Polymorphic base for ChatMessage parts ("type" discriminator).</summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(TextMessagePart), "text")]
    [JsonDerivedType(typeof(ImageUrlMessagePart), "image_url")]
    [JsonDerivedType(typeof(ImageBase64MessagePart), "image_base64")]
    public abstract record MessagePart;

    /// <summary>Plain text part of a chat message.</summary>
    public sealed record TextMessagePart(string Text) : MessagePart;

    /// <summary>Remote image part; the only image mode allowed for remote clients.</summary>
    public sealed record ImageUrlMessagePart(string Url) : MessagePart;

    /// <summary>Base64 image part for embedded binary payloads.</summary>
    public sealed record ImageBase64MessagePart(string Base64, string MediaType) : MessagePart;

    /// <summary>Mirror of ChatMessage wire schema (role + polymorphic parts).</summary>
    public sealed class GatewayChatMessage
    {
        public string Role { get; set; } = "user";
        public List<MessagePart> Parts { get; set; } = new();
        public string? Content { get; set; }

        /// <summary>Creates a system-role message with a single text part.</summary>
        public static GatewayChatMessage System(string text) =>
            new() { Role = "system", Parts = new List<MessagePart> { new TextMessagePart(text) } };

        /// <summary>Creates a user-role message with a single text part.</summary>
        public static GatewayChatMessage User(string text) =>
            new() { Role = "user", Parts = new List<MessagePart> { new TextMessagePart(text) } };
    }
}