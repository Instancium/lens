using System.Text;
using InstantAIGate.Client.Chat;
using Instancium.Lens.Core;
using Microsoft.Extensions.Logging;

namespace Instancium.Lens.Chat;

/// <summary>
/// RAG orchestration. Storage-agnostic: knows only IRetrievalService and IChatClient.
/// Context overflow mitigation (RollbackSession) is deferred to Phase 5 —
/// overflow surfaces here as chat.completion_failed.
/// </summary>
public sealed class ChatService : IChatService
{
    private readonly IRetrievalService _retrieval;
    private readonly IChatClient _chatClient;
    private readonly IQueryBuilder _queryBuilder;
    private readonly ChatOptions _options;
    private readonly ILogger<ChatService> _logger;

    /// <summary>
    /// Initializes the chat service with required dependencies.
    /// </summary>
    public ChatService(
        IRetrievalService retrieval,
        IChatClient chatClient,
        IQueryBuilder queryBuilder,
        ChatOptions? options = null,
        ILogger<ChatService>? logger = null)
    {
        _retrieval = retrieval;
        _chatClient = chatClient;
        _queryBuilder = queryBuilder;
        _options = options ?? new ChatOptions();
        _options.Validate();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatService>.Instance;
    }

    /// <inheritdoc />
    public Task<Result<ChatAnswer>> AskAsync(
        string profileId,
        string question,
        CancellationToken cancellationToken = default) =>
        AskCoreAsync(profileId, question, progress: null, streaming: false, cancellationToken);

    /// <inheritdoc />
    public Task<Result<ChatAnswer>> AskStreamingAsync(
        string profileId,
        string question,
        IProgress<ChatProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        AskCoreAsync(profileId, question, progress, streaming: true, cancellationToken);

    private async Task<Result<ChatAnswer>> AskCoreAsync(
        string profileId,
        string question,
        IProgress<ChatProgress>? progress,
        bool streaming,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        try
        {
            var query = _queryBuilder.Build(question);
            _logger.LogDebug("Asking profile {ProfileId}: {Query}", profileId, query);
            progress?.Report(new ChatProgress(ChatStage.Retrieving, null));
            var retrieved = await _retrieval
                .RetrieveAsync(profileId, query, _options.TopK, cancellationToken)
                .ConfigureAwait(false);
            if (retrieved.IsFailure)
            {
                return Result<ChatAnswer>.Failure(retrieved.Error!);
            }

            var context = retrieved.Value!;
            if (context.Count == 0)
            {
                return Result<ChatAnswer>.Failure(new LensError(
                    "chat.no_context",
                    "No indexed context found. Index a folder before asking questions."));
            }

            var prompt = PromptBuilder.Build(context, question);
            progress?.Report(new ChatProgress(ChatStage.Generating, null));
            var answerText = streaming
                ? await CompleteWithStreamingAsync(prompt, progress, cancellationToken).ConfigureAwait(false)
                : await _chatClient.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(answerText))
            {
                return Result<ChatAnswer>.Failure(new LensError(
                    "chat.empty_response", "The model returned an empty response."));
            }

            _logger.LogInformation("Answered with {CitationCount} citations", context.Count);
            return Result<ChatAnswer>.Success(new ChatAnswer(answerText, BuildCitations(context)));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Question cancelled");
            return Result<ChatAnswer>.Failure(
                new LensError("chat.cancelled", "The question was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Question failed");
            return Result<ChatAnswer>.Failure(
                new LensError("chat.completion_failed", ex.Message));
        }
    }

    private async Task<string> CompleteWithStreamingAsync(
        string prompt,
        IProgress<ChatProgress>? progress,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        await _chatClient.StreamAsync(
            prompt,
            token =>
            {
                builder.Append(token);
                progress?.Report(new ChatProgress(ChatStage.Generating, token));
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return builder.ToString();
    }

    private static IReadOnlyList<Citation> BuildCitations(IReadOnlyList<RetrievedChunk> context)
    {
        var citations = new List<Citation>(context.Count);
        for (var i = 0; i < context.Count; i++)
        {
            var chunk = context[i];
            citations.Add(new Citation(i + 1, chunk.Source, chunk.ChunkIndex, chunk.Text));
        }

        return citations;
    }
}
