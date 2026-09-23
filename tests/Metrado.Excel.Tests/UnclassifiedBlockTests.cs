using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c> "Unclassified Block": uncoded elements "SHALL be
/// written into an explicit, clearly labelled unclassified block, separate from
/// the coded capitulos", listing "each element with its <c>UniqueId</c>, category,
/// family and type", and they "MUST NOT be dropped" nor "silently merged into a
/// coded partida".
/// <para>
/// The second scenario is the one that is easy to lose: "GIVEN a model where every
/// element resolves to a code ... THEN the unclassified block is present and
/// explicitly reports zero entries". A block that disappears when it is empty is
/// not making that claim. It leaves the reader unable to tell "nothing was
/// uncoded" from "nobody looked", and those are different facts about the model.
/// </para>
/// </summary>
public sealed class UnclassifiedBlockTests
{
    private const string BudgetSheet = "Metrado";
    private const string UnclassifiedSheet = "Unclassified";
    private const int BudgetHeaderRow = 2;
    private const int EntryHeaderRow = 3;

    [Fact]
    public void TheBlockIsLabelledAndNamesTheColumnsThatLocateAnElementInTheModel()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(TakeoffFixture.Line("w-1", "C1010")));

        IXLWorksheet sheet = workbook.Worksheet(UnclassifiedSheet);

        Assert.Equal(["Unclassified lines"], WrittenWorkbook.RowText(sheet, 1));
        Assert.Equal("Count", sheet.Cell(2, 1).GetString());
        Assert.Equal(
            ["UniqueId", "Category", "Family", "Type", "Material"],
            WrittenWorkbook.RowText(sheet, EntryHeaderRow));
    }

    [Fact]
    public void UncodedElementsAreListedInTheirOwnBlockAndNotInTheCodedCapitulos()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(FortyWallsOfWhichTwelveAreUncoded());

        Assert.Equal(12, Entries(workbook).Count);
        Assert.Equal(28, CodedMeasurementLines(workbook).Count);
    }

    [Fact]
    public void TheReportedCountIsTheNumberOfEntriesBeneathIt()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(FortyWallsOfWhichTwelveAreUncoded());

        IXLWorksheet sheet = workbook.Worksheet(UnclassifiedSheet);

        Assert.Equal(12, sheet.Cell(2, 2).GetDouble());
        Assert.Equal(Entries(workbook).Count, (int)sheet.Cell(2, 2).GetDouble());
    }

    [Fact]
    public void TheBlockIsStillPresentAndExplicitlyReportsZeroWhenEverythingWasCoded()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010"),
                TakeoffFixture.Line("w-2", "C2020")));

        IXLWorksheet sheet = workbook.Worksheet(UnclassifiedSheet);

        Assert.Equal(
            ["UniqueId", "Category", "Family", "Type", "Material"],
            WrittenWorkbook.RowText(sheet, EntryHeaderRow));
        Assert.Equal(0, sheet.Cell(2, 2).GetDouble());
        Assert.Empty(Entries(workbook));
    }

    [Fact]
    public void ARunThatMeasuredNothingStillCarriesTheBlockReportingZero()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf());

        IXLWorksheet sheet = workbook.Worksheet(UnclassifiedSheet);

        Assert.Equal(0, sheet.Cell(2, 2).GetDouble());
        Assert.Equal(
            ["Level", "Capitulo", "Partida", "UniqueId", "Metrado", "Unit", "Boundary mode", "Material", "Layers"],
            WrittenWorkbook.RowText(workbook.Worksheet(BudgetSheet), BudgetHeaderRow));
    }

    [Fact]
    public void EachEntryCarriesTheIdentityAUserNeedsToGoAndCodeTheElement()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010"),
                TakeoffFixture.Line(
                    "slab-7",
                    UnclassifiedResolver.Code,
                    capitulo: "Floors",
                    family: "Floor",
                    typeName: "Generic 300mm"),
                TakeoffFixture.Line(
                    "wall-4",
                    UnclassifiedResolver.Code,
                    family: "Basic Wall",
                    typeName: "Exterior Brick")));

        Assert.Equal(
            [
                ("slab-7", "Floors", "Floor", "Generic 300mm"),
                ("wall-4", "Walls", "Basic Wall", "Exterior Brick"),
            ],
            Entries(workbook));
    }

    /// <summary>
    /// The specification's own fixture: 12 of 40 walls resolve as unclassified.
    /// </summary>
    private static TakeoffResult FortyWallsOfWhichTwelveAreUncoded() =>
        TakeoffFixture.ResultOf(
            [
                .. Enumerable
                    .Range(1, 28)
                    .Select(n => TakeoffFixture.Line($"coded-{n:D2}", "C1010")),
                .. Enumerable
                    .Range(1, 12)
                    .Select(n => TakeoffFixture.Line(
                        $"uncoded-{n:D2}",
                        UnclassifiedResolver.Code)),
            ]);

    private static IReadOnlyList<(string, string, string, string)> Entries(XLWorkbook workbook)
    {
        IXLWorksheet sheet = workbook.Worksheet(UnclassifiedSheet);

        return
        [
            .. WrittenWorkbook
                .BodyRows(sheet, EntryHeaderRow)
                .Select(row => (
                    Text(sheet, row, "UniqueId"),
                    Text(sheet, row, "Category"),
                    Text(sheet, row, "Family"),
                    Text(sheet, row, "Type"))),
        ];
    }

    private static IReadOnlyList<string> CodedMeasurementLines(XLWorkbook workbook)
    {
        IXLWorksheet sheet = workbook.Worksheet(BudgetSheet);

        return
        [
            .. WrittenWorkbook
                .BodyRows(sheet, BudgetHeaderRow)
                .Where(row =>
                    WrittenWorkbook.Text(sheet, BudgetHeaderRow, row, "Level") == "LINEA")
                .Select(row => WrittenWorkbook.Text(sheet, BudgetHeaderRow, row, "UniqueId")),
        ];
    }

    private static string Text(IXLWorksheet sheet, int row, string header) =>
        WrittenWorkbook.Text(sheet, EntryHeaderRow, row, header);
}
