using InstanciumLens.AI.Contracts;

namespace InstanciumLens.AI.Services
{
    /// <summary>Base failure type for gateway transport and business errors.</summary>
    public class GatewayException : Exception
    {
        public GatewayException(string message) : base(message)
        {
        }

        public GatewayException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>Raised when a session was evicted server-side (reaper, reconnect, drop) and must be re-joined.</summary>
    public sealed class GatewaySessionInactiveException : GatewayException
    {
        public GatewaySessionInactiveException(string message) : base(message)
        {
        }
    }

    /// <summary>Raised on structured KV-context overflow; carries metrics for rollback decisions.</summary>
    public sealed class GatewayContextOverflowException : GatewayException
    {
        public ContextOverflowInfo Info { get; }

        public GatewayContextOverflowException(ContextOverflowInfo info)
            : base($"Context overflow in session {info.SessionId}: past={info.PastTokens}, incoming={info.IncomingTokens}, reserved={info.ReservedTokens}, limit={info.ContextSize}.")
        {
            Info = info;
        }
    }
}