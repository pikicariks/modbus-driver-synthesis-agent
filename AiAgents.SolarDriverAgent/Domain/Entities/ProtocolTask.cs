using AiAgents.SolarDriverAgent.Domain.Enums;

namespace AiAgents.SolarDriverAgent.Domain.Entities;

/// <summary>
/// Predstavlja zadatak za generisanje drajvera iz PDF specifikacije.
/// </summary>
public class ProtocolTask
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Naziv invertera/uređaja.
    /// </summary>
    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>
    /// Originalni PDF fajl (binarne podatke).
    /// </summary>
    public byte[] PdfDocument { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Ekstraktovana Modbus specifikacija iz PDF-a.
    /// </summary>
    public string? ExtractedSpecification { get; private set; }

    /// <summary>
    /// Trenutni status zadatka.
    /// </summary>
    public ProtocolTaskStatus Status { get; private set; }

    /// <summary>
    /// Broj pokušaja generisanja.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Maksimalni broj pokušaja prije odustajanja.
    /// </summary>
    public int MaxAttempts { get; private set; } = 5;

    /// <summary>
    /// Vrijeme kreiranja zadatka.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Vrijeme posljednje izmjene.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Generisani drajver kod (ako postoji).
    /// </summary>
    public DriverCode? CurrentDriver { get; private set; }

    /// <summary>
    /// Istorija simulacija i grešaka.
    /// </summary>
    public ICollection<SimulationLog> SimulationLogs { get; private set; } = new List<SimulationLog>();

    private ProtocolTask() { } // EF Core

    public static ProtocolTask Create(string deviceName, byte[] pdfDocument)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            throw new ArgumentException("Device name is required", nameof(deviceName));

        if (pdfDocument == null || pdfDocument.Length == 0)
            throw new ArgumentException("PDF document is required", nameof(pdfDocument));

        return new ProtocolTask
        {
            Id = Guid.NewGuid(),
            DeviceName = deviceName,
            PdfDocument = pdfDocument,
            Status = ProtocolTaskStatus.Queued,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Postavlja ekstraktovanu specifikaciju iz PDF-a.
    /// </summary>
    public void SetExtractedSpecification(string specification)
    {
        ExtractedSpecification = specification;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Označava zadatak kao "u obradi".
    /// </summary>
    public void MarkAsProcessing()
    {
        Status = ProtocolTaskStatus.Processing;
        AttemptCount++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Vraća zadatak u Queued status bez trošenja retry pokušaja.
    /// Koristi se kada je problem infrastrukturni (Python servis nedostupan).
    /// </summary>
    public void RevertToQueued()
    {
        if (Status == ProtocolTaskStatus.Processing && AttemptCount > 0)
        {
            AttemptCount--;
        }
        Status = ProtocolTaskStatus.Queued;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Označava zadatak kao uspješno završen.
    /// </summary>
    public void MarkAsSuccess(DriverCode driver)
    {
        Status = ProtocolTaskStatus.Success;
        CurrentDriver = driver ?? throw new ArgumentNullException(nameof(driver));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Označava zadatak kao neuspješan.
    /// Napomena: SimulationLog se dodaje u LearnAsync, ne ovdje.
    /// </summary>
    public void MarkAsFailed(string? errorMessage = null)
    {
        Status = ProtocolTaskStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
        // SimulationLog se dodaje u LearnAsync fazi - izbjegavamo duplikate
    }

    /// <summary>
    /// Provjerava da li zadatak može biti ponovo obrađen.
    /// </summary>
    public bool CanRetry() => Status == ProtocolTaskStatus.Failed && AttemptCount < MaxAttempts;

    /// <summary>
    /// Forsira status na Failed (za recovery zaglavljenih taskova).
    /// </summary>
    public void ForceFailedStatus()
    {
        Status = ProtocolTaskStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Provjerava da li je zadatak spreman za obradu.
    /// </summary>
    public bool IsEligibleForProcessing() =>
        Status == ProtocolTaskStatus.Queued ||
        (Status == ProtocolTaskStatus.Failed && CanRetry());
}
