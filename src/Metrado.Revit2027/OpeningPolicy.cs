namespace Metrado.Revit2027;

/// <summary>What kind of element an insert is, as far as measuring it goes.</summary>
public enum InsertKind
{
    FamilyInstance = 1,
    RectangularOpening = 2,
    EmbeddedWall = 3,
    Other = 4,
}

/// <summary>A range along the wall's axis (U) and in height (Z), in feet.</summary>
public sealed record Box(double UMin, double UMax, double ZMin, double ZMax);

/// <summary>
/// What was observed about one insert of a wall, with no Revit type in it.
/// </summary>
/// <param name="CutsWall">The insert generates faces of this wall's geometry.</param>
/// <param name="HostedByWall">This wall, not a joined one, hosts the insert.</param>
/// <param name="IsVoidCut">An unattached void instance cutting the wall.</param>
/// <param name="CutoutSquareFeet">The area of the insert's outline in the wall, when one could be computed.</param>
/// <param name="Outline">The outline's range along the wall, when one could be computed.</param>
public sealed record InsertFacts(
    string UniqueId,
    InsertKind Kind,
    bool CutsWall,
    bool HostedByWall,
    bool IsVoidCut,
    double? CutoutSquareFeet,
    string? CutoutError,
    Box? Outline);

/// <param name="Extent">The wall solid's own range, which joins and attachments can move off the location line.</param>
/// <param name="Conditions">Wall conditions under which an outline no longer matches Revit's deduction.</param>
public sealed record WallFacts(Box Extent, IReadOnlyList<string> Conditions);

public sealed record OpeningDecision(
    IReadOnlyList<OpeningReading> Measured,
    IReadOnlyList<UnmeasuredOpening> Unmeasured);

/// <summary>
/// Decides which of a wall's openings are measured by their outline and
/// which are only reported.
///
/// No read-only Revit API returns the area Revit subtracted for one insert,
/// so a measured area is always an outline standing in for it. Whenever the
/// outline cannot be trusted to equal the deduction, the opening is reported
/// instead: its deduction stands and it is never added back. The error may
/// understate a metrado; it can never invent material Revit did not remove.
/// </summary>
public static class OpeningPolicy
{
    /// <summary>Numerical slack only, in feet: about 0.3 nanometres.</summary>
    private const double Tolerance = 1e-9;

    public static OpeningDecision Decide(WallFacts wall, IReadOnlyList<InsertFacts> inserts)
    {
        ArgumentNullException.ThrowIfNull(wall);
        ArgumentNullException.ThrowIfNull(inserts);

        List<UnmeasuredOpening> unmeasured = [];
        List<InsertFacts> trusted = [];

        // An insert that generates none of this wall's faces did not cut it:
        // hosts share their inserts with joined walls in the listing only.
        foreach (InsertFacts insert in inserts.Where(insert => insert.CutsWall))
        {
            if (Distrust(wall, insert) is string reason)
            {
                unmeasured.Add(new UnmeasuredOpening(insert.UniqueId, reason));
            }
            else
            {
                trusted.Add(insert);
            }
        }

        HashSet<string> overlapping = [.. trusted
            .SelectMany(a => trusted.Where(b => b != a && Overlap(a.Outline!, b.Outline!)).Select(_ => a.UniqueId))];

        foreach (InsertFacts insert in trusted.Where(insert => overlapping.Contains(insert.UniqueId)))
        {
            unmeasured.Add(new UnmeasuredOpening(
                insert.UniqueId, "its outline overlaps another opening's, and Revit deducts their union only once"));
        }

        return new OpeningDecision(
            [.. trusted.Where(insert => !overlapping.Contains(insert.UniqueId))
                .Select(insert => new OpeningReading(insert.UniqueId, insert.CutoutSquareFeet!.Value))],
            [.. unmeasured.OrderBy(opening => inserts.ToList().FindIndex(insert => insert.UniqueId == opening.UniqueId))]);
    }

    private static string? Distrust(WallFacts wall, InsertFacts insert) => insert switch
    {
        { IsVoidCut: true } => "a void cut, which Revit may deduct for only part of the wall's depth",
        { Kind: InsertKind.EmbeddedWall } => "an embedded wall, for which Revit exposes no outline",
        { Kind: InsertKind.Other } => "an element of a kind whose opening cannot be measured",
        { HostedByWall: false } => "it is hosted by another wall and cuts this one only through their join",
        _ when wall.Conditions.Count > 0 => $"this wall's outline no longer matches Revit's deduction: {string.Join("; ", wall.Conditions)}",
        { CutoutSquareFeet: null } => insert.CutoutError ?? "no outline could be computed",
        { CutoutSquareFeet: double area } when !(area > 0) || !double.IsFinite(area) => "its outline has no area",
        { Outline: null } => "its outline's position is unknown",
        _ when !Inside(insert.Outline!, wall.Extent) => "its outline reaches beyond the wall",
        _ => null,
    };

    private static bool Inside(Box outline, Box wall) =>
        outline.UMin >= wall.UMin - Tolerance && outline.UMax <= wall.UMax + Tolerance
        && outline.ZMin >= wall.ZMin - Tolerance && outline.ZMax <= wall.ZMax + Tolerance;

    /// <summary>A shared area, not a shared edge: touching outlines do not overlap.</summary>
    private static bool Overlap(Box a, Box b) =>
        Math.Min(a.UMax, b.UMax) - Math.Max(a.UMin, b.UMin) > Tolerance
        && Math.Min(a.ZMax, b.ZMax) - Math.Max(a.ZMin, b.ZMin) > Tolerance;
}
