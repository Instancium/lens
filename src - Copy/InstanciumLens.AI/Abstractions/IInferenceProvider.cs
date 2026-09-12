using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InstanciumLens.AI.Abstractions
{
    /// <summary>
    /// Defines the contract for an underlying AI inference engine (e.g., Llama.cpp, ONNX).
    /// </summary>
    public interface IInferenceProvider : IDisposable
    {
        /// <summary>
        /// Loads the model weights into RAM/VRAM.
        /// </summary>
        /// <param name="modelPath">Absolute path to the local model file (e.g., .gguf or .onnx).</param>
        /// <param name="cancellationToken">Token to abort initialization.</param>
        Task InitializeAsync(string modelPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a prompt to the model and streams the raw text response back token by token.
        /// </summary>
        /// <param name="prompt">The formatted prompt string.</param>
        /// <param name="cancellationToken">Token to abort generation.</param>
        /// <returns>An asynchronous stream of generated text tokens.</returns>
        IAsyncEnumerable<string> GenerateTextStreamAsync(string prompt, CancellationToken cancellationToken = default);
    }
}