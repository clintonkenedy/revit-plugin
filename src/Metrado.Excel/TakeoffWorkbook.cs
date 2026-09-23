using System.Globalization;
using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel;

/// <summary>
/// Writes a measured model out as the budget workbook: capitulo, partida and
/// linea de medicion.
/// </summary>
public static class TakeoffWorkbook
{
    /// <summary>The sheet holding the coded budget.</summary>
    public const string BudgetSheetName = "Metrado";

    /// <summary>
    /// The sheet holding the elements no link of the codification chain could
    /// code.
    /// </summary>
    /// <remarks>
    /// A sheet of its own because the specification requires the block to be
    /// "separate from the coded capitulos": an uncoded element belongs to no
    /// partida, so a budget that listed it among the coded ones would be claiming
    /// a code it does not have.
    /// </remarks>
    public const string UnclassifiedSheetName = "Unclassified";

    private const int SummaryRow = 1;
    private const int HeaderRow = 2;

    private const int SummaryCountColumn = 2;

    private const int LevelColumn = 1;
    private const int CapituloColumn = 2;
    private const int PartidaColumn = 3;
    private const int UniqueIdColumn = 4;
    private const int MetradoColumn = 5;
    private const int UnitColumn = 6;
    private const int BoundaryModeColumn = 7;

    /// <summary>A layer line's material, and the layers it covers (I3); blank on a whole element's line.</summary>
    private const int MaterialColumn = 8;

    private const int LayersColumn = 9;

    private const string CapituloLevel = "CAPITULO";
    private const string PartidaLevel = "PARTIDA";
    private const string LineaLevel = "LINEA";

    private const int UnclassifiedLabelRow = 1;
    private const int UnclassifiedCountRow = 2;
    private const int UnclassifiedHeaderRow = 3;

    private const int EntryUniqueIdColumn = 1;
    private const int EntryCategoryColumn = 2;
    private const int EntryFamilyColumn = 3;
    private const int EntryTypeColumn = 4;

    private const int EntryMaterialColumn = 5;

    /// <summary>Writes <paramref name="result"/> into <paramref name="destination"/>.</summary>
    /// <remarks>
    /// The stream is written but not closed, so a caller that owns it can keep
    /// using it — the tests read the produced bytes straight back.
    /// </remarks>
    public static void Write(TakeoffResult result, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(destination);

        using XLWorkbook workbook = new();

        WriteBudget(workbook.Worksheets.Add(BudgetSheetName), result);
        WriteUnclassified(workbook.Worksheets.Add(UnclassifiedSheetName), result);

        workbook.SaveAs(destination);
    }

    /// <summary>
    /// Writes the coded budget, above it the count of the measurement lines it
    /// carries.
    /// </summary>
    /// <remarks>
    /// The specification requires an export over a model with no measurable
    /// elements to produce a workbook "containing the headers and an explicit
    /// zero-count summary" that "states that zero measurement lines were exported".
    /// An empty sheet does not state that — it is an absence, and the reader cannot
    /// tell a run that measured nothing from an export that never got this far.
    /// Written unconditionally for the same reason the unclassified count is.
    /// <para>
    /// The count is of the measurement rows on this sheet, so it reconciles with
    /// the rows beneath it exactly as the subtotals do. Uncoded elements were
    /// measured but were not exported as budget lines, and they are counted on
    /// their own sheet — one count per block, each answerable from the block it
    /// heads.
    /// </para>
    /// </remarks>
    private static void WriteBudget(IXLWorksheet sheet, TakeoffResult result)
    {
        sheet.Cell(SummaryRow, LevelColumn).Value = "Measurement lines exported";
        sheet.Cell(SummaryRow, SummaryCountColumn).Value = ExportedLines(result);

        sheet.Cell(HeaderRow, LevelColumn).Value = "Level";
        sheet.Cell(HeaderRow, CapituloColumn).Value = "Capitulo";
        sheet.Cell(HeaderRow, PartidaColumn).Value = "Partida";
        sheet.Cell(HeaderRow, UniqueIdColumn).Value = "UniqueId";
        sheet.Cell(HeaderRow, MetradoColumn).Value = "Metrado";
        sheet.Cell(HeaderRow, UnitColumn).Value = "Unit";
        sheet.Cell(HeaderRow, BoundaryModeColumn).Value = "Boundary mode";
        sheet.Cell(HeaderRow, MaterialColumn).Value = "Material";
        sheet.Cell(HeaderRow, LayersColumn).Value = "Layers";

        int row = HeaderRow + 1;

        foreach (IGrouping<string, Partida> capitulo in ByCapitulo(result))
        {
            sheet.Cell(row, LevelColumn).Value = CapituloLevel;
            sheet.Cell(row, CapituloColumn).Value = capitulo.Key;
            if (capitulo.Select(partida => partida.Unit).Distinct().Count() == 1)
            {
                sheet.Cell(row, MetradoColumn).Value = CapituloTotal(capitulo);
                sheet.Cell(row, UnitColumn).Value = capitulo.First().Unit.Symbol();
            }

            row++;

            foreach (Partida partida in capitulo)
            {
                sheet.Cell(row, LevelColumn).Value = PartidaLevel;
                sheet.Cell(row, CapituloColumn).Value = capitulo.Key;
                sheet.Cell(row, PartidaColumn).Value = partida.Key.PartidaCode;
                sheet.Cell(row, MetradoColumn).Value = partida.Total.Value;
                sheet.Cell(row, UnitColumn).Value = partida.Unit.Symbol();
                row++;

                foreach (Linea linea in InOrder(partida))
                {
                    sheet.Cell(row, LevelColumn).Value = LineaLevel;
                    sheet.Cell(row, CapituloColumn).Value = capitulo.Key;
                    sheet.Cell(row, PartidaColumn).Value = partida.Key.PartidaCode;
                    sheet.Cell(row, UniqueIdColumn).Value = linea.Element.UniqueId;
                    sheet.Cell(row, MetradoColumn).Value = linea.Metrado.Metrado.Value;
                    sheet.Cell(row, UnitColumn).Value = linea.Metrado.Metrado.Unit.Symbol();
                    sheet.Cell(row, BoundaryModeColumn).Value = Name(linea.Metrado.AppliedMode);
                    if (linea.Layer is MaterialLayers layer)
                    {
                        sheet.Cell(row, MaterialColumn).Value = layer.Material.MaterialName;
                        sheet.Cell(row, LayersColumn).Value = Describe(layer);
                    }
                    row++;
                }
            }
        }
    }

    /// <summary>
    /// How the workbook spells a boundary convention.
    /// </summary>
    /// <remarks>
    /// The specification and the criteria file both name the modes
    /// <c>exclusive</c> and <c>inclusive</c>, and a reviewer reads the workbook
    /// against the criteria that produced it. Rendering the enum member instead
    /// would give one convention two spellings.
    /// <para>
    /// Written on the measurement line, which is where the fact already lives.
    /// Repeating it on the capitulo row would be a second home for one value, and
    /// the run report — which the completion dialog shows without opening the
    /// workbook — already carries the per-capitulo convention.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The mode names no declared convention. The export stops rather than
    /// shipping a budget whose convention the workbook cannot state: an
    /// unattributed metrado is exactly the ambiguity reporting the mode exists to
    /// remove.
    /// </exception>
    private static string Name(BoundaryMode mode) => mode switch
    {
        BoundaryMode.Exclusive => "exclusive",
        BoundaryMode.Inclusive => "inclusive",
        _ => throw new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "Not a declared boundary mode, so the workbook cannot record which "
                + "convention produced the metrado."),
    };

    /// <summary>
    /// Writes the uncoded elements, whether or not there are any.
    /// </summary>
    /// <remarks>
    /// The label, the count and the column headers are written unconditionally, so
    /// a run where everything resolved to a code still states that zero elements
    /// were left uncoded. Omitting the block when it is empty would leave the
    /// reader unable to tell "nothing was uncoded" from "this export never looked",
    /// and those are different facts about the model.
    /// </remarks>
    private static void WriteUnclassified(IXLWorksheet sheet, TakeoffResult result)
    {
        IReadOnlyList<Linea> uncoded = Uncoded(result);

        sheet.Cell(UnclassifiedLabelRow, EntryUniqueIdColumn).Value = "Unclassified elements";
        sheet.Cell(UnclassifiedCountRow, EntryUniqueIdColumn).Value = "Count";
        sheet.Cell(UnclassifiedCountRow, EntryCategoryColumn).Value = uncoded.Count;

        sheet.Cell(UnclassifiedHeaderRow, EntryUniqueIdColumn).Value = "UniqueId";
        sheet.Cell(UnclassifiedHeaderRow, EntryCategoryColumn).Value = "Category";
        sheet.Cell(UnclassifiedHeaderRow, EntryFamilyColumn).Value = "Family";
        sheet.Cell(UnclassifiedHeaderRow, EntryTypeColumn).Value = "Type";
        sheet.Cell(UnclassifiedHeaderRow, EntryMaterialColumn).Value = "Material";

        int row = UnclassifiedHeaderRow + 1;

        foreach (Linea linea in uncoded)
        {
            sheet.Cell(row, EntryUniqueIdColumn).Value = linea.Element.UniqueId;
            sheet.Cell(row, EntryCategoryColumn).Value = linea.Element.CategoryName;
            sheet.Cell(row, EntryFamilyColumn).Value = linea.Element.FamilyName;
            sheet.Cell(row, EntryTypeColumn).Value = linea.Element.TypeName;
            if (linea.Layer is MaterialLayers layer)
            {
                sheet.Cell(row, EntryMaterialColumn).Value = layer.Material.MaterialName;
            }

            row++;
        }
    }

    /// <summary>
    /// The lines of every partida the codification chain could not code, ordered
    /// the same way the budget is.
    /// </summary>
    private static IReadOnlyList<Linea> Uncoded(TakeoffResult result) =>
        [
            .. result
                .Partidas
                .Where(partida => partida.IsUnclassified)
                .SelectMany(partida => partida.Lineas)
                .OrderBy(linea => linea.Element.CategoryName, StringComparer.Ordinal)
                .ThenBy(linea => linea.Element.UniqueId, StringComparer.Ordinal)
                .ThenBy(linea => linea.Layer?.FirstPosition ?? -1),
        ];

    /// <summary>
    /// The partidas arranged into capitulos, both in the order the workbook must
    /// present them.
    /// </summary>
    /// <remarks>
    /// Sorting before grouping is what puts the capitulos in order too: grouping
    /// keeps the order each key first appears in, so an already-sorted sequence
    /// yields sorted groups.
    /// <para>
    /// Every comparison is ordinal. The default string comparer is culture-aware,
    /// which would make the row order of a budget a property of the machine that
    /// exported it — and the requirement is that identical input produces identical
    /// ordering, not ordering identical to the exporter's locale.
    /// </para>
    /// </remarks>
    private static IEnumerable<IGrouping<string, Partida>> ByCapitulo(TakeoffResult result) =>
        result
            .Partidas
            .Where(partida => !partida.IsUnclassified)
            .OrderBy(partida => partida.Key.Capitulo, StringComparer.Ordinal)
            .ThenBy(partida => partida.Key.PartidaCode, StringComparer.Ordinal)
            // A partida split by unit: its blocks in the units' declared order.
            .ThenBy(partida => partida.Unit)
            .GroupBy(partida => partida.Key.Capitulo, StringComparer.Ordinal);

    /// <summary>The measurement lines of a partida, in the order they are written.</summary>
    /// <remarks>
    /// <c>UniqueId</c> is the stable per-line key: unique per element and
    /// non-empty by construction. A layered element puts one line per material
    /// under one host <c>UniqueId</c>, so its lines are ordered by where each
    /// material first appears, exterior (or top) first. That orders them totally:
    /// a structure holds each position once and each layer has one material, so
    /// no two materials of one host share a first layer, and ties never fall
    /// back to arrival order.
    /// </remarks>
    private static IEnumerable<Linea> InOrder(Partida partida) =>
        partida.Lineas
            .OrderBy(linea => linea.Element.UniqueId, StringComparer.Ordinal)
            .ThenBy(linea => linea.Layer?.FirstPosition ?? -1);

    /// <summary>
    /// A layer line's layers, exterior (or top) first: each function as the
    /// criteria file spells it and its width in millimetres, invariant, so a
    /// Spanish machine never writes a decimal comma. A measured material's
    /// layers all have a function: reconciliation refuses one without.
    /// </summary>
    private static string Describe(MaterialLayers layer) =>
        string.Join(" + ", layer.Layers.Select(compound => string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1:0.###} mm{2}",
            compound.Function,
            compound.Width.Value * 1000,
            compound.MaterialFromCategory ? " (category material)" : string.Empty)));

    /// <summary>
    /// How many measurement lines the budget sheet exports.
    /// </summary>
    /// <remarks>
    /// Counted off the same filtered sequence the rows are written from, so the
    /// stated figure cannot drift from the rows it heads.
    /// </remarks>
    private static int ExportedLines(TakeoffResult result) =>
        ByCapitulo(result).Sum(capitulo => capitulo.Sum(partida => partida.Lineas.Count));

    /// <summary>The sum of a capitulo's partida totals.</summary>
    /// <remarks>
    /// Written at full double precision rather than rounded, so the subtotal a
    /// reviewer checks reconciles with the lines above it to floating-point
    /// accuracy and the "stated rounding tolerance" the specification allows is
    /// never spent by the writer.
    /// <para>
    /// The partida totals themselves come from <see cref="Partida.Total"/>, which
    /// refuses to add lines measured in different units. A capitulo is totalled
    /// only when its partidas share one unit, which one criterion per category
    /// guarantees; otherwise its row states no total rather than one that adds
    /// m2 to u (<c>excel-budget-export</c>, "Unit Reported per Partida").
    /// </para>
    /// </remarks>
    private static double CapituloTotal(IEnumerable<Partida> partidas) =>
        partidas.Sum(partida => partida.Total.Value);
}
