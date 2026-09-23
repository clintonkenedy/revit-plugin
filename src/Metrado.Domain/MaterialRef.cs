namespace Metrado.Domain;

/// <summary>
/// Identifies the material a layer quantity belongs to. A null reference on a
/// <see cref="RawQuantity"/> means the quantity describes the whole element.
/// </summary>
/// <remarks>
/// Present from I1 so the seam does not change shape when material-layer takeoff
/// arrives in I3, which populates it.
/// <para>
/// The properties are get-only rather than <c>init</c>: these records are
/// extraction facts, and a get-only member removes the <c>with</c> copy path
/// instead of leaving it as a hole the constructor guard cannot see.
/// </para>
/// </remarks>
public sealed record MaterialRef
{
    /// <param name="materialId">The material's UniqueId, stable across sessions as an ElementId is not.</param>
    /// <param name="codes">
    /// The codes the material itself carries, its Keynote and the nominated
    /// shared parameter, by which a layer line is coded. An Assembly Code is a
    /// type's, so a material's is null. None read when omitted.
    /// </param>
    public MaterialRef(string materialId, string materialName, CodificationReadings? codes = null)
    {
        MaterialId = Guard.RequiredText(materialId, nameof(materialId));
        MaterialName = Guard.RequiredText(materialName, nameof(materialName));
        Codes = codes ?? new CodificationReadings(null, null, new Dictionary<string, string?>());
    }

    public string MaterialId { get; }

    public string MaterialName { get; }

    public CodificationReadings Codes { get; }
}
