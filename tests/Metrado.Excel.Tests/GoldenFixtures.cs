using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// The data sets the stored reference workbooks were written from.
/// </summary>
/// <remarks>
/// Defined in one place because the assertion and the regeneration tool must be
/// looking at the same model. A golden regenerated from data that has drifted from
/// the data the assertion replays is not a reference, it is a second opinion.
/// <para>
/// Every value here is fixed and derived from the element index, never from the
/// clock, the environment or a random source: a fixture that could not be
/// reproduced on another machine could not be frozen to disk in the first place.
/// </para>
/// </remarks>
internal static class GoldenFixtures
{
    /// <summary>The specification's own fixture: 12 of 40 walls resolve as unclassified.</summary>
    internal const string TwelveOfFortyName = "twelve-of-forty-unclassified.xlsx";

    /// <summary>A model where every element resolved to a code.</summary>
    internal const string ZeroUnclassifiedName = "zero-unclassified.xlsx";

    /// <summary>A model containing no elements in any supported category.</summary>
    internal const string EmptyResultName = "empty-result.xlsx";

    /// <summary>Every stored reference workbook, by file name.</summary>
    internal static IReadOnlyDictionary<string, TakeoffResult> All =>
        new Dictionary<string, TakeoffResult>(StringComparer.Ordinal)
        {
            [TwelveOfFortyName] = TwelveOfForty(),
            [ZeroUnclassifiedName] = ZeroUnclassified(),
            [EmptyResultName] = EmptyResult(),
        };

    /// <summary>
    /// Forty measured walls and floors, twelve of which no link of the chain could
    /// code, spread over two capitulos and three partidas.
    /// </summary>
    /// <remarks>
    /// Deliberately wider than the scenario's minimum. A reference workbook is only
    /// worth storing if it exercises the arrangement decisions — two capitulos so
    /// the capitulo ordering shows, three partidas so the subtotals nest, both
    /// boundary conventions so the mode column is not uniform, and uncoded elements
    /// under both capitulos so the block's own ordering shows.
    /// </remarks>
    internal static TakeoffResult TwelveOfForty() =>
        TakeoffFixture.ResultOf(
            [
                .. Enumerable.Range(1, 10).Select(n => Coded(n, "C1010", "Walls")),
                .. Enumerable.Range(11, 8).Select(n => Coded(n, "C2020", "Walls")),
                .. Enumerable.Range(19, 10).Select(n => Coded(n, "C3030", "Floors")),
                .. Enumerable.Range(1, 12).Select(Uncoded),
            ]);

    /// <summary>Two capitulos, three partidas, nothing left uncoded.</summary>
    internal static TakeoffResult ZeroUnclassified() =>
        TakeoffFixture.ResultOf(
            [
                .. Enumerable.Range(1, 4).Select(n => Coded(n, "C1010", "Walls")),
                .. Enumerable.Range(5, 3).Select(n => Coded(n, "C2020", "Walls")),
                .. Enumerable.Range(8, 2).Select(n => Coded(n, "C3030", "Floors")),
            ]);

    /// <summary>A run that measured nothing at all.</summary>
    internal static TakeoffResult EmptyResult() => TakeoffFixture.ResultOf();

    private static Linea Coded(int n, string partidaCode, string capitulo) =>
        TakeoffFixture.Line(
            uniqueId: $"{capitulo.ToLowerInvariant()}-{n:D2}",
            partidaCode: partidaCode,
            metrado: n * 1.25,
            capitulo: capitulo,
            appliedMode: n % 3 == 0 ? BoundaryMode.Inclusive : BoundaryMode.Exclusive);

    private static Linea Uncoded(int n) =>
        TakeoffFixture.Line(
            uniqueId: $"uncoded-{n:D2}",
            partidaCode: UnclassifiedResolver.Code,
            metrado: n * 0.5,
            capitulo: n % 2 == 0 ? "Floors" : "Walls",
            family: n % 2 == 0 ? "Floor" : "Basic Wall",
            typeName: n % 2 == 0 ? "Generic 300mm" : "Exterior Brick");
}
