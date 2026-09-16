namespace Metrado.Domain;

/// <summary>
/// The terminal link of the codification chain. It resolves every element it is
/// given, which is what makes codification total.
/// </summary>
/// <remarks>
/// Being unclassified is a reported outcome, not an error and not a dropped row.
/// This link exists so an element nobody could code still carries a code into
/// grouping and reaches the export with its <c>UniqueId</c>, category, family and
/// type, where the user can find it and fix the model.
/// <para>
/// It reads nothing off the element on purpose. Every link that inspects something
/// has already declined by the time control reaches here, so inspecting again could
/// only reach a different conclusion than the link whose job it was.
/// </para>
/// </remarks>
public sealed class UnclassifiedResolver : ICodeResolver
{
    /// <summary>The code an element resolves to when no link could code it.</summary>
    /// <remarks>
    /// Non-blank on purpose: <see cref="PartidaKey"/> rejects a blank code, so a
    /// terminal link answering with empty text would throw during grouping and turn
    /// the ordinary case — a model with some uncoded elements — into a failed export.
    /// <para>
    /// Exposed as a constant so grouping and the workbook's unclassified block name
    /// this group the same way, rather than each repeating a literal that can drift.
    /// </para>
    /// </remarks>
    public const string Code = "UNCLASSIFIED";

    /// <inheritdoc />
    public string? Resolve(ElementTakeoff element) => Code;
}
