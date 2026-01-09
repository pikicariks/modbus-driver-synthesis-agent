namespace AiAgents.SolarDriverAgent.Application.Contracts;

/// <summary>
/// Validator za PDF dokumente.
/// Provjerava strukturu i validnost prije upisivanja u bazu.
/// </summary>
public interface IPdfValidator
{
    /// <summary>
    /// Validira PDF dokument.
    /// </summary>
    PdfValidationResult Validate(byte[] pdfDocument);
}

/// <summary>
/// Rezultat PDF validacije.
/// </summary>
public record PdfValidationResult
{
    /// <summary>
    /// Da li je PDF validan.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Poruka o grešci ako nije validan.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Broj stranica u PDF-u (ako je validan).
    /// </summary>
    public int? PageCount { get; init; }

    /// <summary>
    /// Veličina fajla u bajtovima.
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// PDF verzija (npr. "1.4", "1.7").
    /// </summary>
    public string? PdfVersion { get; init; }

    /// <summary>
    /// Da li PDF sadrži tekst (nije skeniran/image-only).
    /// </summary>
    public bool HasExtractableText { get; init; }

    public static PdfValidationResult Valid(int pageCount, long fileSize, string? pdfVersion, bool hasText) => new()
    {
        IsValid = true,
        PageCount = pageCount,
        FileSize = fileSize,
        PdfVersion = pdfVersion,
        HasExtractableText = hasText
    };

    public static PdfValidationResult Invalid(string errorMessage, long fileSize = 0) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage,
        FileSize = fileSize
    };
}
