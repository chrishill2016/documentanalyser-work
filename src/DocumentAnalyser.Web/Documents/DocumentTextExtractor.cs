using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace DocumentAnalyser.Web.Documents;

/// <summary>
/// Turns an uploaded file into plain text for the model. Deliberately simple: no OCR, no chunking.
/// (Hackathon idea: swap this for Azure AI Content Understanding / Document Intelligence in Foundry.)
/// </summary>
public static class DocumentTextExtractor
{
    public static readonly string[] SupportedExtensions = [".txt", ".md", ".csv", ".json", ".pdf", ".docx"];

    public static async Task<string> ExtractAsync(IFormFile file, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        stream.Position = 0;

        return extension switch
        {
            ".pdf" => ExtractPdf(stream),
            ".docx" => ExtractDocx(stream),
            ".txt" or ".md" or ".csv" or ".json" => Encoding.UTF8.GetString(stream.ToArray()),
            _ => throw new NotSupportedException(
                $"'{extension}' files aren't supported. Use one of: {string.Join(", ", SupportedExtensions)}"),
        };
    }

    private static string ExtractPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine($"--- Page {page.Number} ---");
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private static string ExtractDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            sb.AppendLine(paragraph.InnerText);
        }
        return sb.ToString();
    }
}
