namespace Metrado.Domain;

/// <summary>
/// Every code the adapter managed to read off an element, before the codification
/// chain decides which one wins.
/// </summary>
/// <remarks>
/// A null or blank code is a legitimate reading — it means the parameter was
/// absent or left empty — so these stay nullable and are not guarded. Deciding
/// what an absent code means belongs to the chain, not to the DTO.
/// <para>
/// <c>SharedParameters</c> is guarded only against null: an element with no
/// shared parameters read carries an empty dictionary, and the chain iterates it
/// unconditionally.
/// </para>
/// </remarks>
public sealed record CodificationReadings
{
    public CodificationReadings(
        string? assemblyCode,
        string? keynote,
        IReadOnlyDictionary<string, string?> sharedParameters)
    {
        AssemblyCode = assemblyCode;
        Keynote = keynote;
        SharedParameters = Guard.RequiredValue(sharedParameters, nameof(sharedParameters));
    }

    public string? AssemblyCode { get; }

    public string? Keynote { get; }

    public IReadOnlyDictionary<string, string?> SharedParameters { get; }
}
