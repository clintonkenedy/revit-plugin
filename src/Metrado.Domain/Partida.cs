namespace Metrado.Domain;

/// <summary>
/// One partida: every measured instance that resolved to the same code inside the
/// same capitulo, together with the total those lines add up to.
/// </summary>
/// <remarks>
/// The lines of one partida are not required to come from one element type. Two
/// distinct types resolving to the same code produce a single partida holding the
/// instances of both, which is the mapping <c>partida-codification</c> requires.
/// </remarks>
public sealed record Partida
{
    /// <param name="lineas">
    /// The measurement lines belonging to this partida, in the order they were
    /// encountered. Ordering the workbook's rows is the writer's job; this type
    /// only guarantees the order does not come from a hash bucket.
    /// </param>
    public Partida(PartidaKey key, IReadOnlyList<Linea> lineas)
    {
        Key = key;
        Lineas = Guard.RequiredValue(lineas, nameof(lineas));

        if (Lineas.Count == 0)
        {
            throw new ArgumentException(
                $"Partida {key.PartidaCode} in capitulo {key.Capitulo} has no measurement lines, "
                    + "so it has no unit and no total. A partida exists because at least one "
                    + "element resolved to its code.",
                nameof(lineas));
        }

        Unit = RequireOneUnit(key, Lineas);
        Total = new Quantity(Lineas.Sum(linea => linea.Metrado.Metrado.Value), Unit);
    }

    /// <summary>Capitulo plus resolved code — the identity of this partida.</summary>
    public PartidaKey Key { get; }

    /// <summary>The lineas de medicion that resolved to this partida.</summary>
    public IReadOnlyList<Linea> Lineas { get; }

    /// <summary>The unit <see cref="Total"/> is expressed in.</summary>
    public QuantityUnit Unit { get; }

    /// <summary>
    /// The sum of the measurement lines, which is what the workbook's partida
    /// subtotal must reconcile against.
    /// </summary>
    public Quantity Total { get; }

    /// <summary>
    /// Whether this partida is the group holding the elements no link could code.
    /// </summary>
    /// <remarks>
    /// The comparison lives here, once, so the writer's unclassified block and the
    /// run report's unclassified count both read a flag instead of each repeating
    /// a literal that can drift apart from this one.
    /// <para>
    /// It is an ordinal comparison against the terminal link's constant, which
    /// means an element whose Assembly Code literally reads
    /// <c>UNCLASSIFIED</c> is filed here too. That is a known and deliberate limit
    /// of carrying the resolution as a bare string: removing it needs a closed
    /// two-state code resolution, which the codification chain does not yet return.
    /// The consequence is bounded — such an element is still listed individually
    /// with its own identity, so it is visible rather than absorbed into a coded
    /// partida — and it is recorded for the increment that adds the second real
    /// resolver.
    /// </para>
    /// </remarks>
    public bool IsUnclassified =>
        string.Equals(Key.PartidaCode, UnclassifiedResolver.Code, StringComparison.Ordinal);

    /// <summary>
    /// The single unit every line of this partida was measured in.
    /// </summary>
    /// <remarks>
    /// Mixed units are refused rather than added together. Domain never converts,
    /// so summing them would produce a number expressed in no unit at all and the
    /// workbook would print it under whichever unit happened to come first.
    /// <para>
    /// Grouping never hands a partida mixed units: it lists each unit's lines
    /// as a partida of their own and raises a validation warning (I2, "Unit
    /// Reported per Partida"), so this refusal only guards a direct caller.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The lines disagree on the unit.</exception>
    private static QuantityUnit RequireOneUnit(PartidaKey key, IReadOnlyList<Linea> lineas)
    {
        QuantityUnit unit = lineas[0].Metrado.Metrado.Unit;

        foreach (Linea linea in lineas)
        {
            QuantityUnit other = linea.Metrado.Metrado.Unit;

            if (other != unit)
            {
                throw new InvalidOperationException(
                    $"Partida {key.PartidaCode} in capitulo {key.Capitulo} mixes "
                        + $"{Describe(unit)} and {Describe(other)} measurement lines, so it has "
                        + "no single total. Domain never converts between units.");
            }
        }

        return unit;
    }

    /// <summary>
    /// Names a unit without throwing on an undeclared one. The message describing
    /// a fault must not raise a second fault on top of it.
    /// </summary>
    private static string Describe(QuantityUnit unit) =>
        Enum.IsDefined(typeof(QuantityUnit), unit) ? unit.Symbol() : "an undeclared unit";
}
