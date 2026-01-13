using AiAgents.SolarDriverAgent.Application.Contracts;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace AiAgents.SolarDriverAgent.Infrastructure.External;

/// <summary>
/// PDF validator koji koristi magic bytes i PdfPig za validaciju.
/// </summary>
public class PdfValidator : IPdfValidator
{
    private readonly ILogger<PdfValidator> _logger;

    private static readonly byte[] PdfMagicBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
    private const int MinimumPdfSize = 100;
    private const long MaximumPdfSize = 50 * 1024 * 1024;

    public PdfValidator(ILogger<PdfValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PdfValidationResult Validate(byte[] pdfDocument)
    {
        if (pdfDocument == null || pdfDocument.Length == 0)
        {
            return PdfValidationResult.Invalid("PDF document is empty");
        }

        var fileSize = pdfDocument.Length;

        if (fileSize < MinimumPdfSize)
        {
            _logger.LogWarning("PDF too small: {Size} bytes (minimum: {Min})", fileSize, MinimumPdfSize);
            return PdfValidationResult.Invalid(
                $"PDF document is too small ({fileSize} bytes). Minimum size is {MinimumPdfSize} bytes.",
                fileSize);
        }

        if (fileSize > MaximumPdfSize)
        {
            _logger.LogWarning("PDF too large: {Size} bytes (maximum: {Max})", fileSize, MaximumPdfSize);
            return PdfValidationResult.Invalid(
                $"PDF document is too large ({fileSize / 1024 / 1024} MB). Maximum size is {MaximumPdfSize / 1024 / 1024} MB.",
                fileSize);
        }

        if (!HasValidMagicBytes(pdfDocument))
        {
            _logger.LogWarning("Invalid PDF magic bytes. First 10 bytes: {Bytes}",
                BitConverter.ToString(pdfDocument.Take(10).ToArray()));
            return PdfValidationResult.Invalid(
                "Invalid PDF format. File does not start with PDF signature (%PDF-).",
                fileSize);
        }

        var pdfVersion = ExtractPdfVersion(pdfDocument);

        if (!HasValidEofMarker(pdfDocument))
        {
            _logger.LogWarning("PDF missing %%EOF marker - file may be truncated");
            return PdfValidationResult.Invalid(
                "PDF appears to be truncated or corrupted (missing %%EOF marker).",
                fileSize);
        }

        try
        {
            using var stream = new MemoryStream(pdfDocument);
            using var document = PdfDocument.Open(stream);

            var pageCount = document.NumberOfPages;

            if (pageCount == 0)
            {
                _logger.LogWarning("PDF has no pages");
                return PdfValidationResult.Invalid("PDF document has no pages.", fileSize);
            }

            var hasText = false;
            var totalTextLength = 0;

            var pagesToCheck = Math.Min(pageCount, 3);
            for (int i = 1; i <= pagesToCheck; i++)
            {
                var page = document.GetPage(i);
                var pageText = page.Text;
                totalTextLength += pageText?.Length ?? 0;

                if (!string.IsNullOrWhiteSpace(pageText) && pageText.Length > 10)
                {
                    hasText = true;
                    break;
                }
            }

            if (!hasText)
            {
                _logger.LogWarning(
                    "PDF has {Pages} pages but no extractable text (total: {TextLen} chars). May be scanned/image-based.",
                    pageCount, totalTextLength);
            }

            _logger.LogInformation(
                "PDF validation successful: {Pages} pages, {Size} bytes, version {Version}, hasText: {HasText}",
                pageCount, fileSize, pdfVersion ?? "unknown", hasText);

            return PdfValidationResult.Valid(pageCount, fileSize, pdfVersion, hasText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PdfPig failed to open PDF");
            return PdfValidationResult.Invalid(
                $"PDF is corrupted or uses unsupported features: {ex.Message}",
                fileSize);
        }
    }

    /// <summary>
    /// Provjerava da li PDF počinje sa %PDF- magic bytes.
    /// </summary>
    private static bool HasValidMagicBytes(byte[] data)
    {
        if (data.Length < PdfMagicBytes.Length)
            return false;

        for (int i = 0; i < PdfMagicBytes.Length; i++)
        {
            if (data[i] != PdfMagicBytes[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Ekstraktuje PDF verziju iz header-a.
    /// </summary>
    private static string? ExtractPdfVersion(byte[] data)
    {
        if (data.Length < 8)
            return null;

        try
        {
            var header = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(10, data.Length));

            if (header.StartsWith("%PDF-"))
            {
                var versionPart = header.Substring(5);
                var endIndex = versionPart.IndexOfAny(new[] { '\r', '\n', ' ' });
                if (endIndex > 0)
                    return versionPart.Substring(0, endIndex);
                if (versionPart.Length >= 3)
                    return versionPart.Substring(0, 3); // npr. "1.4"
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Provjerava da li PDF ima %%EOF marker blizu kraja.
    /// </summary>
    private static bool HasValidEofMarker(byte[] data)
    {
        var searchLength = Math.Min(1024, data.Length);
        var searchStart = data.Length - searchLength;

        var endPortion = System.Text.Encoding.ASCII.GetString(data, searchStart, searchLength);
        return endPortion.Contains("%%EOF");
    }
}
