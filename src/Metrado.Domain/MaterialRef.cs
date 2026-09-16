namespace Metrado.Domain;

/// <summary>
/// Identifies the material a layer quantity belongs to. A null reference on a
/// <see cref="RawQuantity"/> means the quantity describes the whole element.
/// </summary>
/// <remarks>
/// Present from I1 so the seam does not change shape when material-layer takeoff
/// arrives in I3; nothing in I1 populates it.
/// <para>
/// The properties are get-only rather than <c>init</c>: these records are
/// extraction facts, and a get-only member removes the <c>with</c> copy path
/// instead of leaving it as a hole the constructor guard cannot see.
/// </para>
/// </remarks>
public sealed record MaterialRef
{
    public MaterialRef(string materialId, string materialName)
    {
        MaterialId = Guard.RequiredText(materialId, nameof(materialId));
        MaterialName = Guard.RequiredText(materialName, nameof(materialName));
    }

    public string MaterialId { get; }

    public string MaterialName { get; }
}
