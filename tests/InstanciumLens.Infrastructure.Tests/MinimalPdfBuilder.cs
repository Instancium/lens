using System.IO;
using System.Text;

namespace InstanciumLens.Infrastructure.Tests
{
    public static class MinimalPdfBuilder
    {
        public static void CreateSamplePdf(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Minimal valid PDF structure with readable text stream
            string content = "BT /F1 24 Tf 100 700 Td (Instancium Lens NDA Agreement) Tj ET";
            int contentLength = Encoding.ASCII.GetByteCount(content);

            var sb = new StringBuilder();
            sb.Append("%PDF-1.4\n");

            // Object 1: Catalog
            int offsetObj1 = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            // Object 2: Pages
            int offsetObj2 = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

            // Object 3: Page
            int offsetObj3 = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

            // Object 4: Stream Content
            int offsetObj4 = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append($"4 0 obj\n<< /Length {contentLength} >>\nstream\n{content}\nendstream\nendobj\n");

            // Object 5: Font
            int offsetObj5 = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

            // Cross-reference table
            int xrefOffset = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append("xref\n0 6\n");
            sb.Append("0000000000 65535 f \n");
            sb.Append($"{offsetObj1:D10} 00000 n \n");
            sb.Append($"{offsetObj2:D10} 00000 n \n");
            sb.Append($"{offsetObj3:D10} 00000 n \n");
            sb.Append($"{offsetObj4:D10} 00000 n \n");
            sb.Append($"{offsetObj5:D10} 00000 n \n");

            // Trailer
            sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
            sb.Append(xrefOffset);
            sb.Append("\n%%EOF");

            File.WriteAllText(filePath, sb.ToString(), Encoding.ASCII);
        }
    }
}