namespace Instancium.Lens.App.Services;

using InstantAIGate.Client.Configuration;
using InstantAIGate.Client.Embeddings;
using Instancium.Vectra.Core.Contracts;
using Microsoft.Extensions.Options;

/// <summary>
/// Adapts the gateway SDK embedding capability to the Vectra embedding seam.
/// WHY: the SDK must stay free of vector-store dependencies, while Vectra opens collections
/// synchronously and requires ModelId/Dimension up front; only the composition root knows
/// both contracts, so the projection lives here next to ProfileScopedVectraEngine.
/// </summary>
public sealed class GatewayEmbeddingProvider : IEmbeddingProvider
{
    private readonly IGatewayEmbeddingClient _client;
    private readonly GatewayOptions _options;

    /// <summary>Initializes the adapter with the SDK capability client and bound options.</summary>
    public GatewayEmbeddingProvider(
        IGatewayEmbeddingClient client,
        IOptions<GatewayOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.EmbeddingRepoId);
        if (_options.ExpectedEmbeddingDimension is not > 0)
        {
            throw new InvalidOperationException(
                "Gateway.ExpectedEmbeddingDimension must be configured: " +
                "Vectra requires a synchronous, stable vector dimension.");
        }
    }

    /// <inheritdoc />
    public string ModelId => _options.EmbeddingRepoId;

    /// <inheritdoc />
    public int Dimension => _options.ExpectedEmbeddingDimension!.Value;

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<float>> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var response = await _client
            .EmbedAsync(text, normalize: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Embedding;
    }
}
