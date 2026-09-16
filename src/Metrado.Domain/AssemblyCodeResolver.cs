namespace Metrado.Domain;

/// <summary>
/// The Assembly Code link of the codification chain: it turns what the adapter
/// read off the element type's <c>ASSEMBLY_CODE</c> parameter into either a usable
/// partida code or "no match".
/// </summary>
/// <remarks>
/// Revit-free and side-effect free, like every link. It reads the value the
/// adapter already captured on <see cref="CodificationReadings"/> rather than
/// touching the Revit API, which is what keeps codification unit-testable with no
/// Revit installed (decision D4).
/// <para>
/// The reading is trimmed <em>before</em> it is judged, not after. That ordering
/// is the whole rule: a parameter someone tabbed through holds whitespace, and
/// trimming first makes "whitespace-only is not a code" a consequence of
/// normalising rather than a second condition that can drift away from it.
/// </para>
/// </remarks>
public sealed class AssemblyCodeResolver
{
    /// <summary>
    /// The trimmed Assembly Code, or <c>null</c> when the element carries none.
    /// </summary>
    /// <returns>
    /// <c>null</c> means "no match", which passes control to the next link. It
    /// never means failure: an element with no Assembly Code is ordinary, not
    /// broken.
    /// </returns>
    public string? Resolve(ElementTakeoff element)
    {
        string? trimmed = element.Codes.AssemblyCode?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
