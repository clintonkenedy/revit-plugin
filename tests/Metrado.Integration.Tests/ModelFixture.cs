using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// The model this suite exports, standing in for Revit extraction.
/// </summary>
/// <remarks>
/// Extraction is task 1.21 and it is <c>[win]</c>: it needs Revit 2027.2 on the
/// Windows host, so nothing on macOS can produce an <see cref="ElementTakeoff"/>
/// from a real document. That is precisely what decision D4 bought — the seam is a
/// plain DTO, so the entire pipeline behind it is exercisable here by handing it
/// the same values the adapter will hand it.
/// <para>
/// <b>What this fixture therefore does NOT prove:</b> that Revit's
/// <c>HOST_AREA_COMPUTED</c> really arrives as these numbers, that
/// <c>UnitUtils.ConvertFromInternalUnits</c> was applied, or that openings are
/// enumerated one by one off a real wall. Those are 1.21's assertions on the
/// Windows host. Everything downstream of the DTO is proved here.
/// </para>
/// <para>
/// Every raw area and opening below is taken verbatim from a
/// <c>metrado-measurement</c> scenario, so a failure points at a sentence in the
/// specification rather than at a number somebody invented for a test.
/// </para>
/// </remarks>
internal static class ModelFixture
{
    /// <summary>The quantity source the built-in Walls criterion reads.</summary>
    /// <remarks>
    /// Spelled the same way <see cref="CriteriaSet.Default"/> spells it, because a
    /// fixture that invented its own key would make every element unmeasurable and
    /// the pipeline would report <c>NoSource</c> for a model that is perfectly fine.
    /// </remarks>
    private const string AreaSource = "HOST_AREA_COMPUTED";

    /// <summary>The capitulo every element in this model belongs to.</summary>
    internal const string Capitulo = "Walls";

    /// <summary>The Assembly Code the three coded walls carry.</summary>
    internal const string CodedPartida = "C1010";

    /// <summary>
    /// Raw 18.0 m² with one 0.6 m² opening — <c>metrado-measurement</c>,
    /// "Sub-threshold opening is added back". The wall the headline assertion reads.
    /// </summary>
    internal const string CorrectedByOneOpening = "wall-corrected-a";

    /// <summary>
    /// Raw 15.0 m² with openings of 0.4, 0.9 and 2.5 m² — "Mixed openings on one
    /// element". Their sum is 3.8, so this wall is what separates judging each
    /// opening from judging their total.
    /// </summary>
    internal const string CorrectedByThreeOpenings = "wall-corrected-b";

    /// <summary>
    /// Raw 16.0 m² with one 2.1 m² opening — "Above-threshold opening stays
    /// deducted". The control: its metrado must equal its raw area.
    /// </summary>
    internal const string UncorrectedAboveThreshold = "wall-unchanged";

    /// <summary>
    /// Raw 20.0 m² and no Assembly Code, so the chain's terminal link codes it.
    /// </summary>
    /// <remarks>
    /// Present so the run report's unclassified count and the workbook's
    /// unclassified sheet are exercised by the same run that produces the budget.
    /// A model where everything codes cleanly would leave both reporting zero, and
    /// zero is the value they report when the stage never ran at all.
    /// </remarks>
    internal const string Uncoded = "wall-uncoded";

    private static readonly WallSpec[] Specification =
    [
        new(CorrectedByOneOpening, CodedPartida, RawArea: 18.0, Openings: [0.6]),
        new(CorrectedByThreeOpenings, CodedPartida, RawArea: 15.0, Openings: [0.4, 0.9, 2.5]),
        new(UncorrectedAboveThreshold, CodedPartida, RawArea: 16.0, Openings: [2.1]),
        new(Uncoded, AssemblyCode: null, RawArea: 20.0, Openings: []),
    ];

    /// <summary>The model, as extraction would hand it over.</summary>
    internal static IReadOnlyList<ElementTakeoff> Walls =>
        [.. Specification.Select(ToTakeoff)];

    /// <summary>
    /// The raw Revit area one wall went in carrying.
    /// </summary>
    /// <remarks>
    /// Read back off the same specification the model is built from rather than
    /// restated in the test. "The exported metrado differs from the corresponding
    /// raw area" is only meaningful if both sides name the same wall, and a literal
    /// repeated in the test is a second copy that can drift away from the input.
    /// </remarks>
    internal static double RawAreaOf(string uniqueId) => Find(uniqueId).RawArea;

    /// <summary>The raw areas of the three walls that reach the budget sheet.</summary>
    /// <remarks>
    /// The uncoded wall is excluded because it is not exported as a budget line —
    /// it is listed on the unclassified sheet instead, so including it here would
    /// compare a partida subtotal against a total that has one extra wall in it.
    /// </remarks>
    internal static double RawAreaTotalOfCodedWalls() =>
        Specification.Where(wall => wall.AssemblyCode is not null).Sum(wall => wall.RawArea);

    private static WallSpec Find(string uniqueId) =>
        Specification.SingleOrDefault(wall => wall.UniqueId == uniqueId)
            ?? throw new ArgumentException(
                $"No wall '{uniqueId}' in the fixture. Known walls: "
                    + string.Join(", ", Specification.Select(wall => wall.UniqueId)),
                nameof(uniqueId));

    private static ElementTakeoff ToTakeoff(WallSpec wall) =>
        new(
            UniqueId: wall.UniqueId,
            CategoryName: Capitulo,
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeKey: "Basic Wall:Generic - 200mm",
            Codes: new CodificationReadings(
                assemblyCode: wall.AssemblyCode,
                keynote: null,
                sharedParameters: new Dictionary<string, string?>()),
            Quantities: [new RawQuantity(AreaSource, SquareMetres(wall.RawArea))],
            Openings:
            [
                .. wall.Openings.Select((area, index) =>
                    new OpeningQuantity($"{wall.UniqueId}-opening-{index + 1}", SquareMetres(area))),
            ]);

    private static Quantity SquareMetres(double value) => new(value, QuantityUnit.SquareMetre);

    /// <summary>One wall as the specification describes it, before it becomes a DTO.</summary>
    /// <param name="AssemblyCode">
    /// <c>null</c> means the parameter is empty on this wall, which is an ordinary
    /// model, not a broken one — the codification chain files it as unclassified.
    /// </param>
    /// <param name="Openings">
    /// Individual opening areas, never a total. They are expanded into one
    /// <see cref="OpeningQuantity"/> each so the rule receives them the way decision
    /// D4 requires.
    /// </param>
    private sealed record WallSpec(
        string UniqueId,
        string? AssemblyCode,
        double RawArea,
        IReadOnlyList<double> Openings);
}
