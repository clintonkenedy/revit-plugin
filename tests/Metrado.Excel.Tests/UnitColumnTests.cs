using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c>, "Unit Reported per Partida" (task 2.5): each
/// partida states the unit its total is in, as the category criterion
/// resolved it, and no total adds quantities in different units.
/// </summary>
public sealed class UnitColumnTests
{
    private const string SheetName = "Metrado";

    /// <summary>"Partida carries its unit": a wall partida in m2, a counted door partida in u.</summary>
    [Fact]
    public void EachPartidaRowReportsItsUnit()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(
            TakeoffFixture.Line("w-1", "C1010"),
            TakeoffFixture.Line("d-1", "C1020", metrado: 1, capitulo: "Doors", unit: QuantityUnit.Each)));
        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal("m2", Unit(sheet, Row(sheet, "PARTIDA", "C1010")));
        Assert.Equal("u", Unit(sheet, Row(sheet, "PARTIDA", "C1020")));
    }

    /// <summary>A line is read beside its number, so its unit is on its row too.</summary>
    [Fact]
    public void EachLineRowReportsItsUnit()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(
            TakeoffFixture.Line("r-1", "C2030", metrado: 12.5, capitulo: "Railings", unit: QuantityUnit.Metre)));
        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal("m", Unit(sheet, Row(sheet, "LINEA", "C2030")));
        Assert.Equal("m", Unit(sheet, Row(sheet, "CAPITULO", null)));
    }

    /// <summary>
    /// "Incompatible units are not summed": a capitulo whose partidas came in
    /// two units states no total, rather than one that adds m2 to u.
    /// </summary>
    [Fact]
    public void ACapituloWhosePartidasDifferInUnitStatesNoTotal()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(
            TakeoffFixture.Line("w-1", "C1010", metrado: 10),
            TakeoffFixture.Line("w-2", "C1010", metrado: 1, unit: QuantityUnit.Each)));
        IXLWorksheet sheet = workbook.Worksheet(SheetName);
        int capitulo = Row(sheet, "CAPITULO", null);

        Assert.True(Cell(sheet, capitulo, "Metrado").IsEmpty());
        Assert.True(Cell(sheet, capitulo, "Unit").IsEmpty());
        Assert.Equal(["m2", "u"], Rows(sheet, "PARTIDA").Select(row => Unit(sheet, row)));
    }

    private static string Unit(IXLWorksheet sheet, int row) => Cell(sheet, row, "Unit").GetString();

    private static IXLCell Cell(IXLWorksheet sheet, int row, string header) =>
        sheet.Cell(row, sheet.Row(2).CellsUsed().Single(cell => cell.GetString() == header).Address.ColumnNumber);

    private static int Row(IXLWorksheet sheet, string level, string? partida) =>
        Rows(sheet, level).First(row => partida is null || sheet.Cell(row, 3).GetString() == partida);

    private static IEnumerable<int> Rows(IXLWorksheet sheet, string level) =>
        WrittenWorkbook.BodyRows(sheet, headerRow: 2).Where(row => sheet.Cell(row, 1).GetString() == level);
}
