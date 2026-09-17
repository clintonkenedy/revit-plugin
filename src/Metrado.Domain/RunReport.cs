namespace Metrado.Domain;

/// <summary>
/// What the user is told when a run completes: how much was exported, how much of
/// it is still unclassified, the convention each capitulo was measured under, and
/// the warnings raised along the way.
/// </summary>
/// <remarks>
/// This report has to be readable without opening the workbook, which is why it
/// carries the numbers rather than pointing at the file that contains them.
/// </remarks>
/// <param name="ExportedLines">
/// Every measurement line in the result, unclassified ones included. The user is
/// told "40 exported, 12 unclassified", so the total is the whole run and the
/// unclassified figure is a part of it, not a separate pile beside it.
/// </param>
/// <param name="UnclassifiedCount">
/// How many of those lines nobody could code. This is the number that tells the
/// user the model is not ready.
/// </param>
/// <param name="Applied">
/// The convention each capitulo was actually measured under, one entry per
/// capitulo that produced at least one line.
/// </param>
public sealed record RunReport(
    int ExportedLines,
    int UnclassifiedCount,
    IReadOnlyList<AppliedCriterion> Applied,
    IReadOnlyList<ValidationWarning> Warnings)
{
    /// <summary>Whether the run found nothing it could measure.</summary>
    /// <remarks>
    /// An empty model is not a failure, but it is not a success that found
    /// quantities either, and the difference is invisible in a report that only
    /// carries counts: "0 exported" reads like a completed export. Stating the
    /// condition as its own fact is what stops a run over the wrong model, or over
    /// a model whose categories are all unsupported, from being presented as a
    /// finished budget.
    /// <para>
    /// Being unable to <em>code</em> an element is a different condition entirely —
    /// those elements were measured and are reported by
    /// <see cref="UnclassifiedCount"/>, so a run made up entirely of unclassified
    /// lines did find measurable elements.
    /// </para>
    /// </remarks>
    public bool NoMeasurableElements => ExportedLines == 0;

    /// <summary>How many warnings the run raised.</summary>
    /// <remarks>
    /// Derived rather than stored. A count carried alongside the list it counts is
    /// a second place for the same fact, and the two only ever diverge in the
    /// direction that under-reports.
    /// </remarks>
    public int WarningCount => Warnings.Count;

    /// <summary>Builds the report for a finished run.</summary>
    public static RunReport For(TakeoffResult result, IReadOnlyList<ValidationWarning> warnings)
    {
        IReadOnlyList<Partida> partidas = Guard.RequiredValue(result, nameof(result)).Partidas;

        return new RunReport(
            ExportedLines: result.LineCount,
            UnclassifiedCount: partidas
                .Where(partida => partida.IsUnclassified)
                .Sum(partida => partida.Lineas.Count),
            Applied: AppliedConventions(partidas),
            Warnings: Guard.RequiredValue(warnings, nameof(warnings)));
    }

    /// <summary>
    /// The convention each capitulo was measured under, read off the measured lines
    /// themselves.
    /// </summary>
    /// <remarks>
    /// Deliberately not read from the criteria table. The requirement is that the
    /// run reports what was <em>applied</em>, and the criteria table only says what
    /// was <em>configured</em>. Those are the same value on a correct run and
    /// different values on exactly the runs worth reporting — so sourcing the
    /// report from the configuration would make it agree with itself while
    /// disagreeing with the budget printed next to it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// One capitulo's lines were measured under more than one convention, so there
    /// is no single effective configuration to report for it.
    /// </exception>
    private static IReadOnlyList<AppliedCriterion> AppliedConventions(
        IReadOnlyList<Partida> partidas)
    {
        Dictionary<string, AppliedCriterion> byCapitulo = new(StringComparer.Ordinal);
        List<string> encountered = [];

        foreach (Linea linea in partidas.SelectMany(partida => partida.Lineas))
        {
            string capitulo = linea.Element.CategoryName;

            AppliedCriterion applied = new(
                capitulo,
                linea.Metrado.Metrado.Unit,
                linea.Metrado.AppliedThreshold,
                linea.Metrado.AppliedMode);

            if (byCapitulo.TryGetValue(capitulo, out AppliedCriterion? already))
            {
                if (already != applied)
                {
                    throw new InvalidOperationException(
                        $"Capitulo {capitulo} was measured under more than one convention "
                            + $"({already} and {applied}), so the run has no single effective "
                            + "configuration to report for it.");
                }

                continue;
            }

            byCapitulo.Add(capitulo, applied);
            encountered.Add(capitulo);
        }

        return [.. encountered.Select(capitulo => byCapitulo[capitulo])];
    }
}
