using System;
using System.IO;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace InstanciumLens.Infrastructure.Vision
{
    public sealed class PdfTextLayerReader
    {
        public const int MinimumUsefulTextLength = 40;

        public Task<int> GetPageCountAsync(string filePath)
        {
            return Task.Run(() =>
            {
                using PdfDocument document = PdfDocument.Open(filePath);
                return document.NumberOfPages;
            });
        }

        public Task<string?> ReadPageTextAsync(string filePath, int pageNumber)
        {
            return Task.Run(() =>
            {
                using PdfDocument document = PdfDocument.Open(filePath);
                if (pageNumber < 1 || pageNumber > document.NumberOfPages)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(pageNumber),
                        $"Page {pageNumber} is outside of the 1..{document.NumberOfPages} range of {filePath}.");
                }
                string text = document.GetPage(pageNumber).Text;
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            });
        }
    }
}