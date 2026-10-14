using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InstanciumLens.Infrastructure.Vision;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InstanciumLens.Infrastructure.Tests
{
    public sealed class MultiPagePdfVisionTests : IDisposable
    {
        private const string SampleFileName = "multi_page_sample.pdf";
        private const string PageOneText = "Knowledge graph sample page one";
        private const string PageTwoText = "Knowledge graph sample page two";
        private const string PageThreeText = "Knowledge graph sample page three";
        private const int ExpectedPageCount = 3;
        private const string ExpectedWord = "Knowledge";

        private readonly string _testDir;

        public MultiPagePdfVisionTests()
        {
            _testDir = Path.Combine(
                Path.GetTempPath(),
                "InstanciumLensTests",
                "MultiPagePdfVisionTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }

        [Fact]
        public async Task ExtractTextAsync_GeneratedThreePagePdf_ExtractsAllPages()
        {
            string samplePath = Path.Combine(_testDir, SampleFileName);
            TestPdfBuilder.Create(samplePath, new[] { PageOneText, PageTwoText, PageThreeText });
            var vision = new WindowsDocumentVision(NullLogger<WindowsDocumentVision>.Instance);

            int pageCount = await vision.GetPageCountAsync(samplePath);
            var nodes = (await vision.ExtractTextAsync(samplePath)).ToList();

            Assert.Equal(ExpectedPageCount, pageCount);
            Assert.Equal(ExpectedPageCount, nodes.Count);
            Assert.All(nodes, node => Assert.Contains(ExpectedWord, node.RawText));
        }

        [Fact]
        public async Task ExtractPageAsync_GeneratedThreePagePdf_ReturnsRequestedPageOnly()
        {
            string samplePath = Path.Combine(_testDir, SampleFileName);
            TestPdfBuilder.Create(samplePath, new[] { PageOneText, PageTwoText, PageThreeText });
            var vision = new WindowsDocumentVision(NullLogger<WindowsDocumentVision>.Instance);

            Core.Models.DocumentNode secondPage = await vision.ExtractPageAsync(samplePath, 2);

            Assert.Equal(2, secondPage.PageNumber);
            Assert.Contains(ExpectedWord, secondPage.RawText);
        }
    }
}