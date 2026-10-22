using InstanciumLens.AI.Configuration;
using InstanciumLens.AI.Services;
using InstanciumLens.Core.Models;
using InstanciumLens.Infrastructure.Vision;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace InstanciumLens.Cli
{
    internal static class Program
    {
        private const string ProbeSchemaJson =
            """
            {
              "Entities": [
                { "Type": "Quantity", "Description": "Physical electrical quantity (voltage, current, energy, charge)" },
                { "Type": "Unit", "Description": "Measurement unit (Volt, Ampere, Joule, Coulomb)" },
                { "Type": "Device", "Description": "Physical device or component (battery, generator, wire, motor)" },
                { "Type": "Concept", "Description": "Defined concept or phenomenon (electric circuit, conductor, insulator)" },
                { "Type": "Person", "Description": "Historical person named in the text" }
              ],
              "Relationships": [
                { "RelationType": "MEASURED_IN", "Description": "Quantity is measured in a unit" },
                { "RelationType": "DEFINED_BY", "Description": "Concept or quantity is defined by a formula or statement" },
                { "RelationType": "PART_OF", "Description": "Device or component is part of a system" },
                { "RelationType": "NAMED_AFTER", "Description": "Unit or concept is named after a person" }
              ]
            }
            """;

        private static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== InstanciumLens CLI - Step-by-Step Diagnostics ===");
            Console.WriteLine();

            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
            {
                Console.WriteLine("Usage: InstanciumLens.Cli [path-to-pdf] [gateway-repo-id] [gateway-base-url] [gateway-api-key]");
                Console.WriteLine("If no arguments are provided, settings are loaded from appsettings.json.");
                return 0;
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var cliSettings = configuration.GetSection("Cli");

            string pdfPath = args.Length > 0
                ? ResolvePdfPath(args[0])
                : ResolvePdfPath(cliSettings["PdfPath"] ?? "engin-test.pdf");

            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                Console.WriteLine($"ERROR: File not found: {pdfPath}");
                Console.WriteLine("Please check the 'Cli:PdfPath' in appsettings.json or provide a valid path as the first argument.");
                return 1;
            }

            string repoId = args.Length > 1
                ? args[1]
                : (cliSettings["Gateway:ActiveRepoId"] ?? "qwen3-vl-8b-instruct");

            string baseUrl = args.Length > 2
                ? args[2]
                : (cliSettings["Gateway:BaseUrl"] ?? "http://localhost:5000");

            string apiKey = args.Length > 3
                ? args[3]
                : (cliSettings["Gateway:ApiKey"]
                   ?? Environment.GetEnvironmentVariable("INSTANCIUM_GATEWAY_API_KEY")
                   ?? "ChangeMeSuperSecureAdminApiKey123");

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddConsole();
            });

            var vision = new WindowsDocumentVision(loggerFactory.CreateLogger<WindowsDocumentVision>());

            try
            {
                Console.WriteLine("=== STEP 1: Getting page count ===");
                int pageCount = await vision.GetPageCountAsync(pdfPath);
                Console.WriteLine($"Total pages: {pageCount}");
                Console.WriteLine();

                Console.WriteLine("=== STEP 2: Extracting text from all pages ===");
                List<DocumentNode> pagesList = (await vision.ExtractTextAsync(pdfPath)).ToList();

                foreach (DocumentNode page in pagesList)
                {
                    Console.WriteLine();
                    Console.WriteLine($"--- PAGE {page.PageNumber} ---");
                    Console.WriteLine($"File: {page.FileName}");
                    Console.WriteLine($"Raw text length: {page.RawText?.Length ?? 0} characters");
                    Console.WriteLine();
                    Console.WriteLine("Extracted text:");
                    Console.WriteLine(new string('=', 80));
                    Console.WriteLine(page.RawText ?? "[NO TEXT EXTRACTED]");
                    Console.WriteLine(new string('=', 80));

                    if (!string.IsNullOrWhiteSpace(page.RawText))
                    {
                        string[] lines = page.RawText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        int wordCount = page.RawText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        int nonEmptyLines = lines.Count(line => !string.IsNullOrWhiteSpace(line));
                        Console.WriteLine();
                        Console.WriteLine("Statistics:");
                        Console.WriteLine($"  - Lines: {lines.Length}");
                        Console.WriteLine($"  - Words: {wordCount}");
                        Console.WriteLine($"  - Non-empty lines: {nonEmptyLines}");
                    }

                    if (page.PageNumber < pagesList.Count)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Press ENTER to continue to next page (or Ctrl+C to exit)...");
                        Console.ReadLine();
                    }
                }

                await RunSourceComparisonAsync(pdfPath);

                Console.WriteLine();
                Console.WriteLine("=== STEP 4: Extraction Summary ===");
                int totalChars = pagesList.Sum(page => page.RawText?.Length ?? 0);
                int totalLines = pagesList.Sum(page => page.RawText?.Split('\n').Length ?? 0);
                int averageChars = pagesList.Count > 0 ? totalChars / pagesList.Count : 0;

                Console.WriteLine($"Total pages processed: {pagesList.Count}");
                Console.WriteLine($"Total characters: {totalChars}");
                Console.WriteLine($"Total lines: {totalLines}");
                Console.WriteLine($"Average chars per page: {averageChars}");

                Console.WriteLine();
                Console.WriteLine("=== Potential Issues ===");
                bool issuesFound = false;
                foreach (DocumentNode page in pagesList)
                {
                    if (string.IsNullOrWhiteSpace(page.RawText))
                    {
                        Console.WriteLine($"  Page {page.PageNumber}: No text extracted");
                        issuesFound = true;
                    }
                    else if (page.RawText.Length < 50)
                    {
                        Console.WriteLine($"  Page {page.PageNumber}: Very little text ({page.RawText.Length} chars)");
                        issuesFound = true;
                    }
                }

                if (!issuesFound)
                {
                    Console.WriteLine("  None detected.");
                }

                if (!string.IsNullOrWhiteSpace(repoId))
                {
                    await RunPass2ProbeAsync(pdfPath, baseUrl, repoId, apiKey, vision, loggerFactory);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("=== STEP 5: Pass 2 probe ===");
                    Console.WriteLine("[SKIPPED: pass the gateway repo id as the second argument to enable the LLM extraction probe]");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("=== ERROR ===");
                Console.WriteLine($"Exception: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return 1;
            }
        }

        private static async Task RunSourceComparisonAsync(string pdfPath)
        {
            Console.WriteLine();
            Console.WriteLine("=== STEP 3: Text layer vs OCR comparison (page 1) ===");

            var textLayerReader = new PdfTextLayerReader();
            string? layerText = await textLayerReader.ReadPageTextAsync(pdfPath, 1);
            Console.WriteLine("--- SOURCE A: embedded text layer (PdfPig) ---");
            if (layerText == null)
            {
                Console.WriteLine("[NO TEXT LAYER: document is a scanned image, OCR is the only source]");
            }
            else
            {
                Console.WriteLine($"Chars: {layerText.Length}");
                Console.WriteLine(layerText[..Math.Min(400, layerText.Length)]);
            }

            string outputDir = Path.Combine(AppContext.BaseDirectory, "diag_output");
            Directory.CreateDirectory(outputDir);

            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            PdfDocument pdfDoc = await PdfDocument.LoadFromFileAsync(file);
            if (pdfDoc.PageCount == 0)
            {
                Console.WriteLine("[PDF has no pages, skipping OCR comparison]");
                return;
            }

            using PdfPage page = pdfDoc.GetPage(0);
            using var stream = new InMemoryRandomAccessStream();
            var renderOptions = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)(page.Size.Width * 300.0 / 72.0),
                DestinationHeight = (uint)(page.Size.Height * 300.0 / 72.0)
            };
            await page.RenderToStreamAsync(stream, renderOptions);

            string pngPath = Path.Combine(outputDir, "page1_300dpi.png");
            stream.Seek(0);
            using (FileStream fileStream = File.Create(pngPath))
            {
                await stream.AsStreamForRead().CopyToAsync(fileStream);
            }

            stream.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();
            OcrEngine? ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocrEngine == null)
            {
                Console.WriteLine("[Windows OCR engine is unavailable on this machine]");
                return;
            }

            OcrResult ocrResult = await ocrEngine.RecognizeAsync(bitmap);
            Console.WriteLine();
            Console.WriteLine("--- SOURCE B: Windows OCR at 300 DPI ---");
            Console.WriteLine($"PNG saved: {pngPath}");
            Console.WriteLine($"Chars: {ocrResult.Text.Length}");
            Console.WriteLine(ocrResult.Text[..Math.Min(400, ocrResult.Text.Length)]);
        }

        private static async Task RunPass2ProbeAsync(
            string pdfPath,
            string baseUrl,
            string repoId,
            string apiKey,
            WindowsDocumentVision vision,
            ILoggerFactory loggerFactory)
        {
            Console.WriteLine();
            Console.WriteLine("=== STEP 5: Pass 2 probe (LLM extraction on page 1) ===");

            var settings = new GatewaySettings
            {
                BaseUrl = baseUrl,
                ActiveRepoId = repoId,
                ApiKey = apiKey
            };

            HubConnection connection = new HubConnectionBuilder()
                .WithUrl(settings.HubUrl, options =>
                    options.AccessTokenProvider = () => Task.FromResult<string?>(
                        string.IsNullOrWhiteSpace(settings.ApiKey) ? null : settings.ApiKey))
                .Build();

            var connectionService = new GatewayConnectionService(
                connection,
                settings,
                loggerFactory.CreateLogger<GatewayConnectionService>());
            var gateway = new GatewayHubClient(connection, connectionService);
            var aiService = new GatewayAiService(
                gateway,
                settings,
                loggerFactory.CreateLogger<GatewayAiService>());

            try
            {
                DocumentNode pageNode = await vision.ExtractPageAsync(pdfPath, 1);
                Console.WriteLine($"Page 1 text chars: {pageNode.RawText.Length}");
                Console.WriteLine("Sending to gateway for extraction...");

                KnowledgeGraph segment = await aiService.ExtractEntitiesAsync(pageNode, ProbeSchemaJson);
                Console.WriteLine();
                Console.WriteLine($"Entities: {segment.Entities.Count}");
                foreach (GraphEntity entity in segment.Entities.Take(15))
                {
                    Console.WriteLine($"  [{entity.Type}] {entity.Name} (confidence {entity.ConfidenceScore:F2})");
                }
                Console.WriteLine($"Relationships (Pass 2): {segment.Relationships.Count}");
                Dictionary<string, string> nameById = segment.Entities.ToDictionary(e => e.Id, e => e.Name);
                foreach (GraphRelationship relationship in segment.Relationships.Take(15))
                {
                    string sourceName = nameById.TryGetValue(relationship.SourceEntityId, out string? source)
                        ? source
                        : relationship.SourceEntityId;
                    string targetName = nameById.TryGetValue(relationship.TargetEntityId, out string? target)
                        ? target
                        : relationship.TargetEntityId;
                    Console.WriteLine($"  {sourceName} -{relationship.RelationType}-> {targetName}");
                }

                // Pass 2.5: document-level relation inference
                // Pass 2.5: document-level relation inference
                if (segment.Entities.Count >= 2)
                {
                    Console.WriteLine();
                    Console.WriteLine("=== Pass 2.5: Inferring relationships between extracted entities ===");
                    List<GraphRelationship> inferredRelations = await aiService.InferRelationsAsync(
                        segment.Entities,
                        pageNode.RawText,
                        ProbeSchemaJson);
                    Console.WriteLine($"Inferred relationships: {inferredRelations.Count}");

                    // Diagnostic: dump entity names for debugging
                    Console.WriteLine("Available entities for lookup:");
                    foreach (var entity in segment.Entities)
                    {
                        Console.WriteLine($"  [{entity.Id}] {entity.Name} ({entity.Type})");
                    }

                    foreach (GraphRelationship relationship in inferredRelations.Take(15))
                    {
                        string sourceName = segment.Entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId)?.Name
                            ?? relationship.SourceEntityId;
                        string targetName = segment.Entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId)?.Name
                            ?? relationship.TargetEntityId;
                        Console.WriteLine($"  {sourceName} -{relationship.RelationType}-> {targetName} (confidence {relationship.Weight:F2})");
                    }
                }

            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                Console.WriteLine("[STEP 5 ERROR] Gateway returned 401 Unauthorized.");
                Console.WriteLine("The gateway requires an API key. Copy it from the app: Sidebar -> Settings -> API Key,");
                Console.WriteLine("then pass it as the fourth CLI argument or set INSTANCIUM_GATEWAY_API_KEY.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STEP 5 ERROR] {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                await connectionService.DisposeAsync();
                await connection.DisposeAsync();
            }
        }

        private static string ResolvePdfPath(string inputPath)
        {
            string candidate = Path.GetFullPath(inputPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string fromBaseDirectory = Path.Combine(AppContext.BaseDirectory, inputPath);
            if (File.Exists(fromBaseDirectory))
            {
                return Path.GetFullPath(fromBaseDirectory);
            }

            return candidate;
        }
    }
}