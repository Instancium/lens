using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.App.Models
{
    public class ChatMessage
    {
        /// <summary>
        /// The author of the message: "user", "assistant", or "system".
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// The text content of the message.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Flag to indicate if the message is currently being typed out (streaming).
        /// Useful for UI loading indicators or blinking cursors.
        /// </summary>
        public bool IsStreaming { get; set; } = false;
    }
}
