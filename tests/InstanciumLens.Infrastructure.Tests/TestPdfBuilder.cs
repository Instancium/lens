using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace InstanciumLens.Infrastructure.Tests
{
    /// <summary>
    /// Writes minimal valid PDF files at test time so vision tests never depend on binary fixtures in the repo.
    /// </summary>
    public static class TestPdfBuilder
    {
        public static void Create(string filePath, IReadOnlyList<string> pageTexts)
        {
            if (pageTexts == null) throw new ArgumentNullException(nameof(pageTexts));
            if (pageTexts.Count == 0) throw new ArgumentException("At least one page text is required.", nameof(pageTexts));

            var pdf = new StringBuilder();
            var offsets = new List<int>();
            int firstPageObjectNumber = 4;
            pdf.Append("%PDF-1.4\n");

            offsets.Add(pdf.Length);
            pdf.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            var kids = new StringBuilder();
            for (int i = 0; i < pageTexts.Count; i++)
            {
                if (i > 0)
                {
                    kids.Append(' ');
                }
                kids.Append(CultureInfo.InvariantCulture, $"{firstPageObjectNumber + (2 * i)} 0 R");
            }
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture,
                $"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageTexts.Count} >>\nendobj\n");

            offsets.Add(pdf.Length);
            pdf.Append("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

            for (int i = 0; i < pageTexts.Count; i++)
            {
                int pageObjectNumber = firstPageObjectNumber + (2 * i);
                int contentObjectNumber = pageObjectNumber + 1;
                offsets.Add(pdf.Length);
                pdf.Append(CultureInfo.InvariantCulture,
                    $"{pageObjectNumber} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
                    $"/Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectNumber} 0 R >>\nendobj\n");

                string stream = $"BT /F1 12 Tf 72 770 Td ({Escape(pageTexts[i])}) Tj ET";
                offsets.Add(pdf.Length);
                pdf.Append(CultureInfo.InvariantCulture,
                    $"{contentObjectNumber} 0 obj\n<< /Length {stream.Length} >>\nstream\n{stream}\nendstream\nendobj\n");
            }

            int objectCount = offsets.Count;
            int xrefOffset = pdf.Length;
            pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets)
            {
                pdf.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
            }
            pdf.Append(CultureInfo.InvariantCulture,
                $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            File.WriteAllText(filePath, pdf.ToString(), Encoding.ASCII);
        }

        private static string Escape(string text)
        {
            var ascii = new StringBuilder(text.Length);
            foreach (char symbol in text)
            {
                char safe = symbol is '(' or ')' or '\\' ? ' ' : symbol;
                ascii.Append(safe < ' ' || safe > '~' ? ' ' : safe);
            }
            return ascii.ToString();
        }
    }
}