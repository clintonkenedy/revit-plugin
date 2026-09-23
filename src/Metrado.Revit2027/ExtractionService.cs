using Autodesk.Revit.DB;
using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// Extracts the model's walls as domain takeoffs, together with the warnings
/// extraction itself raises. Reads only: it opens no transaction, so run from
/// a <c>ReadOnly</c> command it cannot change the document even by mistake.
/// </summary>
public static class ExtractionService
{
    /// <param name="UnmeasuredReasons">Why each reported opening was not measured, one entry per opening.</param>
    public sealed record Extraction(
        IReadOnlyList<ElementTakeoff> Elements,
        IReadOnlyList<ValidationWarning> Warnings,
        IReadOnlyList<string> UnmeasuredReasons)
    {
        public int OpeningsMeasured => Elements.Sum(element => element.Openings.Count);
    }

    public static Extraction Extract(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<ElementTakeoff> elements = [];
        List<ValidationWarning> warnings = [];
        List<string> reasons = [];

        foreach (WallReading reading in WallReader.ReadAll(document))
        {
            ElementTakeoff takeoff = WallTakeoff.From(reading, SquareMetres);
            elements.Add(takeoff);
            warnings.AddRange(WallTakeoff.Warnings(takeoff, reading));
            reasons.AddRange(reading.Unmeasured.Select(opening => opening.Reason));
        }

        return new Extraction(elements, warnings, reasons);
    }

    /// <summary>Revit's own conversion from its internal square feet; never a hand-written factor.</summary>
    public static double SquareMetres(double squareFeet) =>
        UnitUtils.ConvertFromInternalUnits(squareFeet, UnitTypeId.SquareMeters);
}
