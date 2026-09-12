using Microsoft.Extensions.Logging;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Services;
using InstanciumLens.Infrastructure.Vision;
using InstanciumLens.Infrastructure.Data;
using InstanciumLens.AI.Abstractions;
using InstanciumLens.AI.Providers.LlamaCpp;
using InstanciumLens.AI.Services;
using InstanciumLens.AI.Downloader;

namespace InstanciumLens.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            // ==========================================
            // 1. HttpClient & Downloader
            // ==========================================
            builder.Services.AddHttpClient("DownloaderClient", client =>
            {
                client.Timeout = TimeSpan.FromHours(3); // Match gateway timeout
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) InstanciumLens");
            });

            // Register as Singleton so the download state survives page navigation!
            builder.Services.AddSingleton<ModelProvisioning>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                var client = factory.CreateClient("DownloaderClient");
                return new ModelProvisioning(client);
            });


            // ==========================================
            // 2. AI & Inference Layer
            // ==========================================
            // Singleton because we only want ONE model loaded in RAM/VRAM at a time
            builder.Services.AddSingleton<IInferenceProvider, LlamaCppProvider>();
            builder.Services.AddTransient<ILocalModelService, LocalAiService>();

            // ==========================================
            // 3. Core & Infrastructure Layer
            // ==========================================
            builder.Services.AddSingleton<IDocumentVision, WindowsDocumentVision>();

            // Factory for SQLite Repository (since it needs a dynamic folder path)
            builder.Services.AddTransient<Func<string, IGraphRepository>>(provider =>
                folderPath => new SqLiteGraphRepository(folderPath));

            builder.Services.AddTransient<DocumentAnalyzer>();

            return builder.Build();
        }
    }
}