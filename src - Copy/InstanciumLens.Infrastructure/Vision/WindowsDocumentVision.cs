using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
// Windows SDK namespaces required for native offline processing
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace InstanciumLens.Infrastructure.Vision
{
    public class WindowsDocumentVision : IDocumentVision
    {
        private readonly OcrEngine _ocrEngine;

        public WindowsDocumentVision()
        {
            // Initialize the native Windows OCR engine. 
            // We use the default language of the OS, ensuring offline capability.
            _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

            if (_ocrEngine == null)
            {
                throw new InvalidOperationException("Native Windows OCR Engine is not supported or missing on this machine.");
            }
        }

        public async Task<IEnumerable<DocumentNode>> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"The file was not found: {filePath}");
            }

            var documentNodes = new List<DocumentNode>();

            // Access the file using Windows Storage API
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));

            // Load the PDF document using native Windows.Data.Pdf
            PdfDocument pdfDoc = await PdfDocument.LoadFromFileAsync(file);
            string fileName = Path.GetFileName(filePath);

            // Iterate through all pages in the PDF
            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using PdfPage page = pdfDoc.GetPage(i);
                using var stream = new InMemoryRandomAccessStream();

                // Render the PDF page to a stream
                await page.RenderToStreamAsync(stream);

                // Decode the stream into a SoftwareBitmap for the OCR engine
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                // Extract text using native Windows.Media.Ocr
                OcrResult ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);

                var node = new DocumentNode
                {
                    FilePath = filePath,
                    FileName = fileName,
                    PageNumber = (int)i + 1,
                    RawText = ocrResult.Text
                };

                documentNodes.Add(node);
            }

            return documentNodes;
        }
    }
}