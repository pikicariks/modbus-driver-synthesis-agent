namespace AiAgents.Core.Abstractions;

/// <summary>
/// Komponenta za učenje - LEARN faza.
/// Snima iskustva i omogućava poboljšanje kroz iteracije.
/// </summary>
/// <typeparam name="TExperience">Tip iskustva koje se pamti</typeparam>
public interface ILearningComponent<TExperience>
    where TExperience : class
{
    /// <summary>
    /// Snima novo iskustvo u memoriju.
    /// </summary>
    Task RecordExperienceAsync(TExperience experience, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dohvata relevantna prošla iskustva za kontekst.
    /// </summary>
    Task<IReadOnlyList<TExperience>> GetRelevantExperiencesAsync(
        string context,
        int maxCount = 5,
        CancellationToken cancellationToken = default);
}
