using InstantAIGate.Client.Chat;
using InstantAIGate.Core.Dtos.Inference;
using Instancium.Lens.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Instancium.Lens.Chat.Tests;

public sealed class ChatServiceTests
{
    private readonly FakeRetrievalService _retrieval = new();
    private readonly FakeChatClient _llm = new();

    private ChatService CreateSut(ChatOptions? options = null) =>
        new(_retrieval, _llm, new QueryBuilder(), options, NullLogger<ChatService>.Instance);

    [Fact]
    public async Task AskAsync_ReturnsAnswerWithCitations()
    {
        _retrieval.Outcome = Result<IReadOnlyList<RetrievedChunk>>.Success(
        [
            new RetrievedChunk("a.md", 0, "alpha"),
            new RetrievedChunk("b.txt", 2, "beta")
        ]);

        var result = await CreateSut().AskAsync("user-1", "What is inside?", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Answer [1]", result.Value!.Text);
        Assert.Equal(2, result.Value.Citations.Count);
        Assert.Equal(1, result.Value.Citations[0].Number);
        Assert.Equal("a.md", result.Value.Citations[0].Source);
        Assert.Equal(0, result.Value.Citations[0].ChunkIndex);
        Assert.Equal("alpha", result.Value.Citations[0].Text);
        Assert.Equal(2, result.Value.Citations[1].Number);
        Assert.Equal("b.txt", result.Value.Citations[1].Source);
        Assert.Equal(2, result.Value.Citations[1].ChunkIndex);
    }

    [Fact]
    public async Task AskAsync_PassesTopKFromOptions()
    {
        var sut = CreateSut(new ChatOptions { TopK = 3 });

        await sut.AskAsync("user-1", "What is inside?", TestContext.Current.CancellationToken);

        Assert.Equal("user-1", _retrieval.LastProfileId);
        Assert.Equal("What is inside?", _retrieval.LastQuery);
        Assert.Equal(3, _retrieval.LastTopK);
    }

    [Fact]
    public async Task AskAsync_PropagatesRetrievalFailure()
    {
        _retrieval.Outcome = Result<IReadOnlyList<RetrievedChunk>>.Failure(
            new LensError("retrieval.search_failed", "boom"));

        var result = await CreateSut().AskAsync("user-1", "What is inside?", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("retrieval.search_failed", result.Error!.Code);
    }

    [Fact]
    public async Task AskAsync_NoContext_FailsWithoutCallingLlm()
    {
        _retrieval.Outcome = Result<IReadOnlyList<RetrievedChunk>>.Success(
            Array.Empty<RetrievedChunk>());

        var result = await CreateSut().AskAsync("user-1", "What is inside?", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("chat.no_context", result.Error!.Code);
        Assert.Null(_llm.LastPrompt);
    }

    [Fact]
    public async Task AskAsync_LlmFailure_MapsToCompletionFailed()
    {
        _llm.Exception = new InvalidOperationException("gateway down");

        var result = await CreateSut().AskAsync("user-1", "What is inside?", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("chat.completion_failed", result.Error!.Code);
    }

    [Fact]
    public async Task AskAsync_EmptyLlmResponse_Fails()
    {
        _llm.Response = "   ";

        var result = await CreateSut().AskAsync("user-1", "What is inside?", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("chat.empty_response", result.Error!.Code);
    }

    [Fact]
    public async Task AskAsync_Cancellation_MapsToCancelled()
    {
        _llm.Exception = new OperationCanceledException();

        var result = await CreateSut().AskAsync("user-1", "What is inside?", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("chat.cancelled", result.Error!.Code);
    }
}

internal sealed class FakeRetrievalService : IRetrievalService
{
    public Result<IReadOnlyList<RetrievedChunk>> Outcome { get; set; } =
        Result<IReadOnlyList<RetrievedChunk>>.Success(
            new[] { new RetrievedChunk("a.md", 0, "alpha") });

    public string? LastProfileId { get; private set; }
    public string? LastQuery { get; private set; }
    public int LastTopK { get; private set; }

    public Task<Result<IReadOnlyList<RetrievedChunk>>> RetrieveAsync(
        string profileId,
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        LastProfileId = profileId;
        LastQuery = query;
        LastTopK = topK;
        return Task.FromResult(Outcome);
    }
}

internal sealed class FakeChatClient : IChatClient
{
    public string Response { get; set; } = "Answer [1]";
    public Exception? Exception { get; set; }
    public string? LastPrompt { get; private set; }
    public string SessionId { get; } = "fake-session";

    public Task<string> CompleteAsync(
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        LastPrompt = message.ToString() ?? string.Empty;
        return Exception is not null
            ? Task.FromException<string>(Exception)
            : Task.FromResult(Response);
    }

    public Task StreamAsync(
        ChatMessage message,
        Func<string, Task> onToken,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
