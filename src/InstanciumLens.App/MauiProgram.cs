// src/InstanciumLens.App/MauiProgram.cs
using InstanciumLens.AI.Abstractions;
using InstanciumLens.AI.Configuration;
using InstanciumLens.AI.Services;
using InstanciumLens.App.Services;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Services;
using InstanciumLens.Infrastructure.Data;
using InstanciumLens.Infrastructure.Services;
using InstanciumLens.Infrastructure.Vision;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

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

            var gatewaySettings = new GatewaySettings();
            gatewaySettings.BaseUrl = Preferences.Default.Get("GatewayBaseUrl", gatewaySettings.BaseUrl);
            gatewaySettings.ApiKey = Preferences.Default.Get("GatewayApiKey", gatewaySettings.ApiKey);
            gatewaySettings.ActiveRepoId = Preferences.Default.Get("GatewayActiveRepoId", gatewaySettings.ActiveRepoId);
            builder.Services.AddSingleton(gatewaySettings);

            builder.Services.AddSingleton(_ => new HubConnectionBuilder()
                .WithUrl(gatewaySettings.HubUrl, options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult<string?>(string.IsNullOrWhiteSpace(gatewaySettings.ApiKey) ? null : gatewaySettings.ApiKey);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build());

            builder.Services.AddSingleton<GatewayConnectionService>();
            builder.Services.AddSingleton<GatewaySessionManager>();
            builder.Services.AddSingleton<IGatewayHubClient, GatewayHubClient>();
            builder.Services.AddSingleton<ILocalModelService, GatewayAiService>();
            builder.Services.AddSingleton<IDocumentVision, WindowsDocumentVision>();
            builder.Services.AddTransient<Func<string, IGraphRepository>>(provider =>
                folderPath => new SqLiteGraphRepository(folderPath));
            builder.Services.AddTransient<DocumentAnalyzer>();
            builder.Services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
            builder.Services.AddSingleton<IFolderAnalyzer, LocalFolderAnalyzer>();
            builder.Services.AddSingleton<IAppStateService, MauiAppStateService>();
            builder.Services.AddTransient<IFolderManifestService, LocalFolderManifestService>();
            builder.Services.AddTransient<IGraphRagManager, GraphRagManager>();
            builder.Services.AddSingleton<IGraphRepositoryFactory, SqLiteGraphRepositoryFactory>();

            return builder.Build();
        }
    }
}