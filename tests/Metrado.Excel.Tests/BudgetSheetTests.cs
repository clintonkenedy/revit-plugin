using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c> "Capitulo, Partida and Linea Hierarchy" requires the
/// workbook to express three grouping levels and states the hard part explicitly:
/// "The grouping level of every row MUST be identifiable from the row itself, not
/// inferred from surrounding formatting." Indentation, bold text and blank spacer
/// rows are all formatting, so none of them satisfies it — a reader looking at one
/// row in isolation has to be able to say what that row is.
/// <para>
/// "Traceability Anchor on Every Measurement Line" adds the dedicated
/// <c>UniqueId</c> column, and "Subtotals Reconcile with Their Lines" requires
/// each partida to total its lines and each capitulo to total its partidas.
/// </para>
/// </summary>
public sealed class BudgetSheetTests
{
    private const string SheetName = "Metrado";
    private const int HeaderRow = 2;

    [Fact]
    public void TheBudgetSheetNamesEveryColumnAReaderNeedsToPlaceARow()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(TakeoffFixture.Line("w-1", "C1010")));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal(
            ["Level", "Capitulo", "Partida", "UniqueId", "Metrado", "Unit", "Boundary mode", "Material", "Layers"],
            WrittenWorkbook.RowText(sheet, HeaderRow));
    }

    [Fact]
    public void EveryRowNamesItsOwnGroupingLevelRatherThanLeavingItToBeInferred()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010"),
                TakeoffFixture.Line("w-2", "C1010"),
                TakeoffFixture.Line("w-3", "C2020")));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal(
            ["CAPITULO", "PARTIDA", "LINEA", "LINEA", "PARTIDA", "LINEA"],
            Levels(sheet));
    }

    [Fact]
    public void EachMeasurementLineNamesThePartidaAndCapituloItBelongsTo()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010"),
                TakeoffFixture.Line("w-2", "C2020"),
                TakeoffFixture.Line("f-1", "C1010", capitulo: "Floors")));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        // "w-1" and "f-1" share the code C1010 under different capitulos, so a row
        // that reported only its code would still be unplaceable.
        Assert.Equal(("Walls", "C1010"), Placement(sheet, "w-1"));
        Assert.Equal(("Walls", "C2020"), Placement(sheet, "w-2"));
        Assert.Equal(("Floors", "C1010"), Placement(sheet, "f-1"));
    }

    [Fact]
    public void EveryMeasurementLineCarriesANonEmptyUniqueId()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("wall-a", "C1010"),
                TakeoffFixture.Line("wall-b", "C1010"),
                TakeoffFixture.Line("wall-c", "C2020"),
                TakeoffFixture.Line("floor-a", "C3030", capitulo: "Floors")));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        // Sorted in the assertion rather than expected in order: row ordering is a
        // separate guarantee with its own tests, and this one is about no line
        // reaching the workbook without the anchor that locates it in the model.
        IReadOnlyList<string> identifiers =
        [
            .. MeasurementRows(sheet)
                .Select(row => Text(sheet, row, "UniqueId"))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(["floor-a", "wall-a", "wall-b", "wall-c"], identifiers);
        Assert.All(identifiers, identifier => Assert.False(string.IsNullOrWhiteSpace(identifier)));
    }

    [Fact]
    public void ThePartidaTotalIsTheSumOfItsMeasurementLines()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010", metrado: 10.5),
                TakeoffFixture.Line("w-2", "C1010", metrado: 4.25),
                TakeoffFixture.Line("w-3", "C1010", metrado: 7.25),
                TakeoffFixture.Line("w-4", "C2020", metrado: 3.5)));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal(22.0, Number(sheet, PartidaRow(sheet, "C1010"), "Metrado"), 9);
        Assert.Equal(3.5, Number(sheet, PartidaRow(sheet, "C2020"), "Metrado"), 9);
    }

    [Fact]
    public void TheCapituloTotalIsTheSumOfItsPartidas()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010", metrado: 10.5),
                TakeoffFixture.Line("w-2", "C1010", metrado: 4.25),
                TakeoffFixture.Line("w-3", "C2020", metrado: 3.25),
                TakeoffFixture.Line("f-1", "C3030", metrado: 2.0, capitulo: "Floors")));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal(18.0, Number(sheet, CapituloRow(sheet, "Walls"), "Metrado"), 9);
        Assert.Equal(2.0, Number(sheet, CapituloRow(sheet, "Floors"), "Metrado"), 9);
    }

    private static IReadOnlyList<string> Levels(IXLWorksheet sheet) =>
        [.. WrittenWorkbook.BodyRows(sheet, HeaderRow).Select(row => Text(sheet, row, "Level"))];

    private static IReadOnlyList<int> MeasurementRows(IXLWorksheet sheet) =>
        [.. RowsAtLevel(sheet, "LINEA")];

    /// <summary>The capitulo and partida a measurement line places itself under.</summary>
    private static (string Capitulo, string Partida) Placement(IXLWorksheet sheet, string uniqueId)
    {
        int row = MeasurementRows(sheet).Single(candidate =>
            Text(sheet, candidate, "UniqueId") == uniqueId);

        return (Text(sheet, row, "Capitulo"), Text(sheet, row, "Partida"));
    }

    private static IEnumerable<int> RowsAtLevel(IXLWorksheet sheet, string level) =>
        WrittenWorkbook
            .BodyRows(sheet, HeaderRow)
            .Where(row => Text(sheet, row, "Level") == level);

    private static int PartidaRow(IXLWorksheet sheet, string partidaCode) =>
        RowsAtLevel(sheet, "PARTIDA").Single(row => Text(sheet, row, "Partida") == partidaCode);

    private static int CapituloRow(IXLWorksheet sheet, string capitulo) =>
        RowsAtLevel(sheet, "CAPITULO").Single(row => Text(sheet, row, "Capitulo") == capitulo);

    private static string Text(IXLWorksheet sheet, int row, string header) =>
        WrittenWorkbook.Text(sheet, HeaderRow, row, header);

    private static double Number(IXLWorksheet sheet, int row, string header) =>
        WrittenWorkbook.Number(sheet, HeaderRow, row, header);
}
