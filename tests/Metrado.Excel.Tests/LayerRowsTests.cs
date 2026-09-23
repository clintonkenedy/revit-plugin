using System.Globalization;
using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c>, "Material-Layer Rows" (task 3.5): a layered
/// element gives one line per material, each naming its material and the
/// layers it covers, and keeping the host's <c>UniqueId</c>; a whole
/// element's line leaves both blank.
/// </summary>
public sealed class LayerRowsTests
{
    private const string Budget = "Metrado";
    private const int HeaderRow = 2;

    [Fact]
    public void ALayerLineNamesItsMaterialAndLayersAndAWholeLineLeavesThemBlank()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(
            TakeoffFixture.LayerLine("walls-01", "02.04.01", 26.22, "Tarrajeo frotachado 1:5", (LayerFunction.Finish1, 0.015), (LayerFunction.Finish2, 0.015)),
            TakeoffFixture.Line("walls-02", "C1010")));
        IXLWorksheet sheet = workbook.Worksheet(Budget);

        int layer = Lines(sheet).Single(row => Text(sheet, row, "Partida") == "02.04.01");
        int whole = Lines(sheet).Single(row => Text(sheet, row, "Partida") == "C1010");

        Assert.Equal(("Tarrajeo frotachado 1:5", "Finish1 15 mm + Finish2 15 mm"), (Text(sheet, layer, "Material"), Text(sheet, layer, "Layers")));
        Assert.Equal((string.Empty, string.Empty), (Text(sheet, whole, "Material"), Text(sheet, whole, "Layers")));
    }

    /// <summary>The spec's scenario: a wall of three materials is three lines under one host UniqueId.</summary>
    [Fact]
    public void AThreeMaterialWallIsThreeLinesUnderOneUniqueId()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(GoldenFixtures.MaterialLayers());
        IXLWorksheet sheet = workbook.Worksheet(Budget);

        List<int> wall = [.. Lines(sheet).Where(row => Text(sheet, row, "UniqueId") == "walls-01")];

        Assert.Equal(3, wall.Count);
        Assert.Equal(3, wall.Select(row => Text(sheet, row, "Material")).Distinct().Count());
    }

    /// <summary>Two lines of one host in one partida come exterior first, whatever order they arrive in.</summary>
    [Fact]
    public void LinesOfOneHostComeExteriorFirst()
    {
        Linea exterior = TakeoffFixture.LayerLine("walls-03", "02.04.01", 13.11, "Tarrajeo frotachado 1:5", (LayerFunction.Finish1, 0.015));
        Linea interior = TakeoffFixture.LayerLine("walls-03", "02.04.01", 13.11, "Tarrajeo impermeabilizado 1:4", (LayerFunction.Finish2, 0.020)) with
        {
            Layer = new MaterialLayers(new MaterialRef("m-inner", "Tarrajeo impermeabilizado 1:4"), [new CompoundLayer(2, LayerFunction.Finish2, new Quantity(0.020, QuantityUnit.Metre), "m-inner")]),
        };

        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(interior, exterior));
        IXLWorksheet sheet = workbook.Worksheet(Budget);

        Assert.Equal(["Tarrajeo frotachado 1:5", "Tarrajeo impermeabilizado 1:4"], Lines(sheet).Select(row => Text(sheet, row, "Material")));
    }

    [Fact]
    public void ReversedInputWritesTheSameWorkbook()
    {
        Linea[] lines = [.. GoldenFixtures.MaterialLayers().Partidas.SelectMany(partida => partida.Lineas)];

        using XLWorkbook inOrder = WrittenWorkbook.Of(TakeoffFixture.ResultOf(lines));
        using XLWorkbook reversed = WrittenWorkbook.Of(TakeoffFixture.ResultOf([.. lines.Reverse()]));

        Assert.Equal(WrittenWorkbook.Grid(inOrder.Worksheet(Budget)), WrittenWorkbook.Grid(reversed.Worksheet(Budget)));
        Assert.Equal(WrittenWorkbook.Grid(inOrder.Worksheet("Unclassified")), WrittenWorkbook.Grid(reversed.Worksheet("Unclassified")));
    }

    /// <summary>An uncoded layer line is listed with its material, so the estimator knows which material to key.</summary>
    [Fact]
    public void AnUncodedLayerLineNamesItsMaterial()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(
            TakeoffFixture.LayerLine("walls-04", UnclassifiedResolver.Code, 13.11, "Metal Stud Layer", (LayerFunction.Structure, 0.092))));
        IXLWorksheet sheet = workbook.Worksheet("Unclassified");

        int row = WrittenWorkbook.BodyRows(sheet, headerRow: 3).Single();

        Assert.Equal("Metal Stud Layer", WrittenWorkbook.Text(sheet, 3, row, "Material"));
    }

    /// <summary>A machine set to a decimal comma (es-ES; es-PE already writes a point) writes the same Layers text.</summary>
    [Fact]
    public void TheLayersTextIsTheSameUnderADecimalCommaCulture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(
                TakeoffFixture.LayerLine("walls-05", "02.06.01", 4.0, "Rigid insulation", (LayerFunction.Insulation, 0.085725))));
            IXLWorksheet sheet = workbook.Worksheet(Budget);

            Assert.Equal("Insulation 85.725 mm", Text(sheet, Lines(sheet).Single(), "Layers"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>A layer given the category's material says so, and the line count counts layer lines.</summary>
    [Fact]
    public void TheCategorysMaterialIsNamedAndLayerLinesAreCounted()
    {
        Linea line = TakeoffFixture.LayerLine("walls-06", "02.01.01", 12.0, "Default Wall", (LayerFunction.Structure, 0.2));
        line = line with { Layer = new MaterialLayers(line.Layer!.Material, [new CompoundLayer(0, LayerFunction.Structure, new Quantity(0.2, QuantityUnit.Metre), line.Layer.Material.MaterialId, materialFromCategory: true)]) };

        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(line, TakeoffFixture.Line("walls-07", "02.01.01")));
        IXLWorksheet sheet = workbook.Worksheet(Budget);

        Assert.Equal("Structure 200 mm (category material)", Text(sheet, Lines(sheet).First(row => Text(sheet, row, "UniqueId") == "walls-06"), "Layers"));
        Assert.Equal(2, sheet.Cell(1, 2).GetValue<int>());
    }

    private static IEnumerable<int> Lines(IXLWorksheet sheet) =>
        WrittenWorkbook.BodyRows(sheet, HeaderRow).Where(row => sheet.Cell(row, 1).GetString() == "LINEA");

    private static string Text(IXLWorksheet sheet, int row, string header) => WrittenWorkbook.Text(sheet, HeaderRow, row, header);
}
