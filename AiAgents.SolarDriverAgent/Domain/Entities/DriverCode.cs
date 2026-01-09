namespace AiAgents.SolarDriverAgent.Domain.Entities;

/// <summary>
/// Predstavlja generisani kod Modbus drajvera.
/// </summary>
public class DriverCode
{
    public Guid Id { get; private set; }

    /// <summary>
    /// ID zadatka za koji je drajver generisan.
    /// </summary>
    public Guid ProtocolTaskId { get; private set; }

    /// <summary>
    /// Generisani C# kod drajvera.
    /// </summary>
    public string SourceCode { get; private set; } = string.Empty;

    /// <summary>
    /// Verzija generisanog koda (inkrementira se sa svakim pokušajem).
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Da li je kod validiran kroz simulaciju.
    /// </summary>
    public bool IsValidated { get; private set; }

    /// <summary>
    /// Vrijeme generisanja.
    /// </summary>
    public DateTime GeneratedAt { get; private set; }

    /// <summary>
    /// Hash koda za detekciju promjena.
    /// </summary>
    public string CodeHash { get; private set; } = string.Empty;

    private DriverCode() { } // EF Core

    public static DriverCode Create(Guid protocolTaskId, string sourceCode, int version)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new ArgumentException("Source code is required", nameof(sourceCode));

        return new DriverCode
        {
            Id = Guid.NewGuid(),
            ProtocolTaskId = protocolTaskId,
            SourceCode = sourceCode,
            Version = version,
            IsValidated = false,
            GeneratedAt = DateTime.UtcNow,
            CodeHash = ComputeHash(sourceCode)
        };
    }

    /// <summary>
    /// Označava kod kao validiran.
    /// </summary>
    public void MarkAsValidated()
    {
        IsValidated = true;
    }

    private static string ComputeHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
