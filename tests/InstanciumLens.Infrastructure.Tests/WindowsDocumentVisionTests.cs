using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InstanciumLens.Infrastructure.Vision;
using Xunit;

namespace InstanciumLens.Infrastructure.Tests
{
    public class WindowsDocumentVisionTests
    {
        [Fact]
        public async Task ExtractTextAsync_ShouldExtractExactText_FromGeneratedPdf()
        {
            // Arrange
            var visionEngine = new WindowsDocumentVision();
            string tempPdfPath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "generated_sample.pdf");

            // Auto-generate the physical PDF with specific text: "Instancium Lens NDA Agreement"
            MinimalPdfBuilder.CreateSamplePdf(tempPdfPath);

            try
            {
                // Act
                var result = await visionEngine.ExtractTextAsync(tempPdfPath, CancellationToken.None);
                var documentNodes = result.ToList();

                // Assert
                Assert.NotNull(documentNodes);
                Assert.NotEmpty(documentNodes);

                var firstPage = documentNodes.First();
                Assert.Equal(1, firstPage.PageNumber);
                Assert.Equal("generated_sample.pdf", firstPage.FileName);
                Assert.False(string.IsNullOrWhiteSpace(firstPage.RawText), "Windows OCR returned empty text.");

                // CRITICAL: Verify the actual OCR engine successfully read the visual text
                Assert.Contains("Instancium Lens NDA Agreement", firstPage.RawText, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempPdfPath))
                {
                    File.Delete(tempPdfPath);
                }
            }
        }

        [Fact]
        public async Task ExtractTextAsync_ShouldThrowFileNotFoundException_ForInvalidPath()
        {
            // Arrange
            var visionEngine = new WindowsDocumentVision();
            string nonExistentPath = Path.Combine(AppContext.BaseDirectory, "MissingFile.pdf");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                visionEngine.ExtractTextAsync(nonExistentPath, CancellationToken.None));
        }
    }
}