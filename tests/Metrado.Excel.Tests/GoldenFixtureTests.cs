using ClosedXML.Excel;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c> "Deterministic Output Ordering" asks for a stored
/// reference to be viable: "GIVEN a stored reference workbook for a fixture data
/// set, WHEN the writer runs over that fixture, THEN the produced content matches
/// the reference."
/// <para>
/// Each fixture is checked twice over. First the STORED workbook is opened and the
/// specification's facts are asserted against it directly — a reference frozen from
/// a broken writer would otherwise be reproduced faithfully forever, and the
/// comparison would agree with a budget nobody should ship. Only then is the
/// writer's current output compared against it, which is what catches drift.
/// </para>
/// </summary>
public sealed class GoldenFixtureTests
{
    private const string BudgetSheet = "Metrado";
    private const string UnclassifiedSheet = "Unclassified";

    private const int SummaryRow = 1;
    private const int BudgetHeaderRow = 2;
    private const int EntryHeaderRow = 3;

    [Fact]
    public void TheStoredReferenceForTwelveOfFortyHoldsTheBudgetTheSpecificationDescribes()
    {
        using XLWorkbook workbook = GoldenWorkbook.Opened(GoldenFixtures.TwelveOfFortyName);

        Assert.Equal(12, Entries(workbook).Count);
        Assert.Equal(28, MeasurementLines(workbook).Count);
        Assert.Equal(28, workbook.Worksheet(BudgetSheet).Cell(SummaryRow, 2).GetDouble());
        Assert.Equal(12, workbook.Worksheet(UnclassifiedSheet).Cell(2, 2).GetDouble());

        // The uncoded elements are the ones missing from the budget, not a separate
        // dozen: 40 measured, 28 exported, and no identifier on both sheets.
        Assert.Empty(Entries(workbook).Select(entry => entry.Item1).Intersect(MeasurementLines(workbook)));
    }

    [Fact]
    public void TheWriterStillReproducesTheTwelveOfFortyReference() =>
        GoldenWorkbook.AssertReproduces(
            GoldenFixtures.TwelveOfFortyName,
            GoldenFixtures.TwelveOfForty());

    [Fact]
    public void TheStoredReferenceForACompletelyCodedModelStillCarriesTheBlockReportingZero()
    {
        using XLWorkbook workbook = GoldenWorkbook.Opened(GoldenFixtures.ZeroUnclassifiedName);

        Assert.Equal(9, MeasurementLines(workbook).Count);
        Assert.Equal(9, workbook.Worksheet(BudgetSheet).Cell(SummaryRow, 2).GetDouble());
        Assert.Equal(0, workbook.Worksheet(UnclassifiedSheet).Cell(2, 2).GetDouble());
        Assert.Empty(Entries(workbook));
    }

    [Fact]
    public void TheWriterStillReproducesTheCompletelyCodedReference() =>
        GoldenWorkbook.AssertReproduces(
            GoldenFixtures.ZeroUnclassifiedName,
            GoldenFixtures.ZeroUnclassified());

    /// <summary>Task 2.5's golden: each partida in its own unit, and a capitulo split by unit stating no total.</summary>
    [Fact]
    public void TheWriterStillReproducesTheMixedUnitsReference() =>
        GoldenWorkbook.AssertReproduces(
            GoldenFixtures.MixedUnitsName,
            GoldenFixtures.MixedUnits());

    [Fact]
    public void TheStoredReferenceForAnEmptyRunCarriesHeadersAndStatesZero()
    {
        using XLWorkbook workbook = GoldenWorkbook.Opened(GoldenFixtures.EmptyResultName);

        IXLWorksheet budget = workbook.Worksheet(BudgetSheet);

        Assert.Equal(
            ["Level", "Capitulo", "Partida", "UniqueId", "Metrado", "Unit", "Boundary mode"],
            WrittenWorkbook.RowText(budget, BudgetHeaderRow));
        Assert.Equal(0, budget.Cell(SummaryRow, 2).GetDouble());
        Assert.Equal(0, workbook.Worksheet(UnclassifiedSheet).Cell(2, 2).GetDouble());
        Assert.Empty(MeasurementLines(workbook));
    }

    [Fact]
    public void TheWriterStillReproducesTheEmptyRunReference() =>
        GoldenWorkbook.AssertReproduces(
            GoldenFixtures.EmptyResultName,
            GoldenFixtures.EmptyResult());

    private static IReadOnlyList<string> MeasurementLines(XLWorkbook workbook)
    {
        IXLWorksheet sheet = workbook.Worksheet(BudgetSheet);

        return
        [
            .. WrittenWorkbook
                .BodyRows(sheet, BudgetHeaderRow)
                .Where(row => WrittenWorkbook.Text(sheet, BudgetHeaderRow, row, "Level") == "LINEA")
                .Select(row => WrittenWorkbook.Text(sheet, BudgetHeaderRow, row, "UniqueId")),
        ];
    }

    private static IReadOnlyList<(string, string, string, string)> Entries(XLWorkbook workbook)
    {
        IXLWorksheet sheet = workbook.Worksheet(UnclassifiedSheet);

        return
        [
            .. WrittenWorkbook
                .BodyRows(sheet, EntryHeaderRow)
                .Select(row => (
                    WrittenWorkbook.Text(sheet, EntryHeaderRow, row, "UniqueId"),
                    WrittenWorkbook.Text(sheet, EntryHeaderRow, row, "Category"),
                    WrittenWorkbook.Text(sheet, EntryHeaderRow, row, "Family"),
                    WrittenWorkbook.Text(sheet, EntryHeaderRow, row, "Type"))),
        ];
    }
}
