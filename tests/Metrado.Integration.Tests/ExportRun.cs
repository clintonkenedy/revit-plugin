using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// One finished export: the criteria it measured under, the grouped budget, the
/// report the user is shown, and the workbook as it was actually written.
/// </summary>
/// <remarks>
/// All four are kept because the assertions worth making are the ones that compare
/// them. A metrado is only interesting next to the raw quantity it came from, and a
/// run report is only interesting next to the workbook it claims to describe.
/// </remarks>
internal sealed record ExportRun(
    EffectiveCriteria Criteria,
    TakeoffResult Result,
    RunReport Report,
    XLWorkbook Workbook) : IDisposable
{
    /// <summary>The budget as a reader sees it, read back out of the written bytes.</summary>
    internal WrittenBudget Budget => new(Workbook);

    public void Dispose() => Workbook.Dispose();

    /// <summary>The measurement line one wall produced.</summary>
    /// <exception cref="InvalidOperationException">
    /// No line, or more than one. In I1 one element produces exactly one line, so
    /// either is a real defect — and taking the first would hide a duplicate behind
    /// an assertion that still passed.
    /// </exception>
    internal Linea LineaOf(string uniqueId)
    {
        Linea[] matched =
        [
            .. Result
                .Partidas
                .SelectMany(partida => partida.Lineas)
                .Where(linea => linea.Element.UniqueId == uniqueId),
        ];

        return matched.Length == 1
            ? matched[0]
            : throw new InvalidOperationException(
                $"Expected exactly one measurement line for '{uniqueId}', found "
                    + $"{matched.Length}.");
    }

    /// <summary>The partida one resolved code produced.</summary>
    internal Partida PartidaOf(string partidaCode) =>
        Result.Partidas.SingleOrDefault(partida => partida.Key.PartidaCode == partidaCode)
            ?? throw new InvalidOperationException(
                $"No partida '{partidaCode}' in the result. Partidas: "
                    + string.Join(
                        ", ",
                        Result.Partidas.Select(partida => partida.Key.PartidaCode)));
}
