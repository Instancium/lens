using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace InstanciumLens.Infrastructure.Vision
{
    public class WindowsDocumentVision : IDocumentVision
    {
        private const double TargetDpi = 300.0;
        private readonly OcrEngine _ocrEngine;
        private readonly ILogger<WindowsDocumentVision> _logger;
        private readonly PdfTextLayerReader _textLayerReader = new();

        public WindowsDocumentVision(ILogger<WindowsDocumentVision>? logger = null)
        {
            _logger = logger ?? NullLogger<WindowsDocumentVision>.Instance;
            _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (_ocrEngine == null)
            {
                throw new InvalidOperationException("Native Windows OCR Engine is not supported or missing on this machine.");
            }
        }

        public async Task<IEnumerable<DocumentNode>> ExtractTextAsync(
            string filePath,
            CancellationToken cancellationToken = default,
            IProgress<DocumentPageProgress>? pageProgress = null)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"The file was not found: {filePath}");
            }

            var documentNodes = new List<DocumentNode>();
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
            PdfDocument pdfDoc = await PdfDocument.LoadFromFileAsync(file);
            string fileName = Path.GetFileName(filePath);
            int pageCount = (int)pdfDoc.PageCount;
            var documentStopwatch = Stopwatch.StartNew();

            _logger.LogInformation("Pass 1 started for {FileName}: {PageCount} pages", fileName, pageCount);

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DocumentNode node = await BuildPageNodeAsync(pdfDoc, i, filePath, fileName);
                documentNodes.Add(node);
                _logger.LogDebug(
                    "Pass 1 page {PageNumber}/{PageCount} extracted for {FileName}",
                    (int)i + 1,
                    pageCount,
                    fileName);
                pageProgress?.Report(new DocumentPageProgress(fileName, (int)i + 1, pageCount));
            }

            _logger.LogInformation(
                "Pass 1 completed for {FileName}: {PageCount} pages in {ElapsedSeconds:F1} s",
                fileName,
                pageCount,
                documentStopwatch.Elapsed.TotalSeconds);
            return documentNodes;
        }

        public async Task<DocumentNode> ExtractPageAsync(string filePath, int pageNumber, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"The file was not found: {filePath}");
            }

            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
            PdfDocument pdfDoc = await PdfDocument.LoadFromFileAsync(file);
            if (pageNumber < 1 || pageNumber > (int)pdfDoc.PageCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pageNumber),
                    $"Page {pageNumber} is outside of the 1..{pdfDoc.PageCount} range of {filePath}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await BuildPageNodeAsync(pdfDoc, (uint)pageNumber - 1, filePath, Path.GetFileName(filePath));
        }

        public async Task<int> GetPageCountAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"The file was not found: {filePath}");
            }

            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
            PdfDocument pdfDoc = await PdfDocument.LoadFromFileAsync(file);
            return (int)pdfDoc.PageCount;
        }

        private async Task<DocumentNode> BuildPageNodeAsync(PdfDocument pdfDoc, uint pageIndex, string filePath, string fileName)
        {
            string? textLayer = null;
            try
            {
                textLayer = await _textLayerReader.ReadPageTextAsync(filePath, (int)pageIndex + 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Page {PageNumber} of {FileName}: text layer read failed, falling back to OCR.",
                    (int)pageIndex + 1,
                    fileName);
            }

            if (!string.IsNullOrWhiteSpace(textLayer) && textLayer.Length >= PdfTextLayerReader.MinimumUsefulTextLength)
            {
                _logger.LogDebug(
                    "Page {PageNumber} of {FileName}: using embedded text layer ({Length} chars).",
                    (int)pageIndex + 1,
                    fileName,
                    textLayer.Length);
                return new DocumentNode
                {
                    FilePath = filePath,
                    FileName = fileName,
                    PageNumber = (int)pageIndex + 1,
                    RawText = textLayer
                };
            }

            _logger.LogDebug(
                "Page {PageNumber} of {FileName}: text layer missing, falling back to OCR.",
                (int)pageIndex + 1,
                fileName);
            return await RenderPageAsync(pdfDoc, pageIndex, filePath, fileName);
        }

        private async Task<DocumentNode> RenderPageAsync(PdfDocument pdfDoc, uint pageIndex, string filePath, string fileName)
        {
            using PdfPage page = pdfDoc.GetPage(pageIndex);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, BuildRenderOptions(page));
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            OcrResult ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
            return new DocumentNode
            {
                FilePath = filePath,
                FileName = fileName,
                PageNumber = (int)pageIndex + 1,
                RawText = ocrResult.Text
            };
        }

        private static PdfPageRenderOptions BuildRenderOptions(PdfPage page)
        {
            return new PdfPageRenderOptions
            {
                DestinationWidth = (uint)(page.Size.Width * TargetDpi / 72.0),
                DestinationHeight = (uint)(page.Size.Height * TargetDpi / 72.0)
            };
        }
    }
}