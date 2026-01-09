namespace AiAgents.SolarDriverAgent.Application.Contracts;

/// <summary>
/// Interfejs za ekstrakciju teksta iz PDF dokumenata.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Ekstraktuje tekst iz PDF dokumenta.
    /// </summary>
    Task<string> ExtractTextAsync(byte[] pdfDocument, CancellationToken cancellationToken = default);
}
