using Instancium.Chunker;
using Instancium.FileParser;
using Instancium.Lens.Chat;
using Instancium.Lens.Ingestion;
using Instancium.Lens.App.Services;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using InstantAIGate.Client.Chat;
using InstantAIGate.Client.Configuration;
using InstantAIGate.Client.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


namespace Instancium.Lens.App;

/// <summary>
/// Composition root. The only place allowed to instantiate the Vectra engine and
/// wire cross-module dependencies (baseline §5.1, Vectra guide §1). The engine is
/// built against the real Instancium.Vectra 1.1.1 surface: VectraEngineFactory.Create
/// + VectraOptions.BaseDirectory. The guide's AddVectraEngine helper does not exist
/// in the shipped package and is intentionally not reproduced.
/// </summary>
public static class MauiProgram
{
    /// <summary>Builds the MAUI application with the full Lens dependency graph.</summary>
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        LoadAppSettings(builder.Configuration);

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        RegisterSharedState(builder.Services);
        RegisterGateway(builder.Services, builder.Configuration);
        RegisterVectra(builder.Services, builder.Configuration);
        RegisterLens(builder.Services, builder.Configuration);

        return builder.Build();
    }

    /// <summary>
    /// Loads the bundled appsettings.json raw asset, then applies environment
    /// overrides (e.g. Gateway__ApiKey) so secrets never land in the repository.
    /// </summary>
    private static void LoadAppSettings(IConfigurationBuilder configuration)
    {
        using Stream packageStream = FileSystem.OpenAppPackageFileAsync("appsettings.json")
            .GetAwaiter().GetResult();
        var buffered = new MemoryStream();
        packageStream.CopyTo(buffered);
        buffered.Position = 0;
        // The configuration system owns the stream for the app lifetime; do not dispose it here.
        configuration.AddJsonStream(buffered);
        configuration.AddEnvironmentVariables();
    }

    private static void RegisterSharedState(IServiceCollection services)
    {
        services.AddSingleton<AppState>();
    }

    /// <summary>
    /// Wires the SDK gateway plane and the Vectra embedding bridge.
    /// WHY: transport, session strategies and diagnostics are owned by the SDK packages;
    /// the composition root only binds configuration and selects the context policy.
    /// </summary>
    private static void RegisterGateway(IServiceCollection services, IConfiguration configuration)
    {
        services.AddInstantAIGateClient(options =>
            configuration.GetSection("Gateway").Bind(options));
        services.AddSingleton<IEmbeddingProvider, GatewayEmbeddingProvider>();
        services.AddSingleton<IChatClient>(sp =>
        {
            GatewayOptions options = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
            IChatClientFactory factory = sp.GetRequiredService<IChatClientFactory>();
            return factory.Create(options.ContextPolicy);
        });
    }

    private static void RegisterVectra(IServiceCollection services, IConfiguration configuration)
    {
        VectraStorageSettings settings = configuration.GetSection("Vectra").Get<VectraStorageSettings>()
            ?? new VectraStorageSettings();
        string defaultBaseDirectory = string.IsNullOrWhiteSpace(settings.BaseDirectory)
            ? Path.Combine(FileSystem.AppDataDirectory, "lens-vectra")
            : settings.BaseDirectory;
        var locationOptions = new VectraLocationOptions
        {
            IndexFolderName = string.IsNullOrWhiteSpace(settings.IndexFolderName)
                ? ".instancium"
                : settings.IndexFolderName,
            DefaultBaseDirectory = defaultBaseDirectory
        };
        services.AddSingleton(new VectraStorageInfo(defaultBaseDirectory));
        services.AddSingleton(locationOptions);
        services.AddSingleton<IIndexLocationRegistry, IndexLocationRegistry>();
        services.AddSingleton<IIndexLocationResolver, IndexLocationResolver>();
        services.AddSingleton<IIndexStateStore, IndexStateStore>();
        services.AddSingleton<IVectraEngine, ProfileScopedVectraEngine>();
    }

    private static void RegisterLens(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFileParser, TextFileParser>();
        services.AddSingleton<IFileParser, MarkdownFileParser>();
        services.AddSingleton<IChunker>(_ =>
        {
            ChunkingOptions chunkingOptions = configuration.GetSection("Chunking").Get<ChunkingOptions>()
                ?? new ChunkingOptions();
            return new TextChunker(chunkingOptions);
        });
        services.AddSingleton<IIngestionService, IngestionService>();

        services.AddSingleton<IQueryBuilder, QueryBuilder>();
        services.AddSingleton<IRetrievalService, VectraRetrievalService>();
        ChatOptions chatOptions = configuration.GetSection("Chat").Get<ChatOptions>()
            ?? new ChatOptions();
        services.AddSingleton(chatOptions);
        services.AddSingleton<IChatService, ChatService>();

#if WINDOWS
        services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
#endif
    }

    /// <summary>
    /// App-side binding shim for the "Vectra" section. The real VectraOptions exposes
    /// engine internals that must not be configuration-bindable; only the storage root
    /// is user-facing.
    /// </summary>
    private sealed record VectraStorageSettings
    {
        /// <summary>Absolute directory for Vectra collections; empty means platform AppData.</summary>
        public string? BaseDirectory { get; init; }

        /// <summary>Index subdirectory inside the scanned folder; empty means ".instancium".</summary>
        public string? IndexFolderName { get; init; }
    }
}
