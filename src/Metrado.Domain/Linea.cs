namespace Metrado.Domain;

/// <summary>
/// One linea de medicion: a single measured element instance together with the
/// partida code the codification chain resolved for it.
/// </summary>
/// <param name="Element">
/// The element this line measures. Carried whole rather than copied field by field
/// because the workbook needs its <c>UniqueId</c>, category, family and type
/// verbatim — for the traceability column and for the unclassified block — and a
/// parallel set of those four fields is four more places for them to drift away
/// from the element they are supposed to name.
/// </param>
/// <param name="PartidaCode">
/// The resolved code, carried on the line itself because
/// <c>excel-budget-export</c> requires every measurement line to identify its
/// partida from the row rather than from surrounding formatting.
/// </param>
/// <param name="Metrado">
/// What the measurement rule produced, including the boundary mode and threshold
/// it actually applied.
/// </param>
/// <param name="Layer">
/// The material this line measures, and the layers it covers, when the
/// element is taken off by material layer (I3); null for a whole element. The
/// host's <c>UniqueId</c> stays the line's anchor either way.
/// </param>
public sealed record Linea(ElementTakeoff Element, string PartidaCode, MetradoResult Metrado, MaterialLayers? Layer = null)
{
    /// <summary>
    /// The partida this line belongs to: its category as capitulo, plus the
    /// resolved code.
    /// </summary>
    /// <remarks>
    /// This is the whole grouping key. Element type identity is deliberately absent
    /// from it, which is what makes two distinct types resolving to one code
    /// collapse into a single partida.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The category or the resolved code is blank. A blank code would surface in
    /// the budget as an unnamed partida, which is why the codification chain
    /// declines blank answers before a line is ever built.
    /// </exception>
    public PartidaKey Key => new(Element.CategoryName, PartidaCode);
}
