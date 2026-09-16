namespace Metrado.Domain;

/// <summary>
/// One link of the codification chain: given an element's extracted data, it
/// returns either a resolved partida code or "no match".
/// </summary>
/// <remarks>
/// <c>null</c> means "no match", never failure. A link that cannot code an element
/// is the ordinary case, not an error.
/// <para>
/// Implementations MUST be free of Revit dependencies and MUST be side-effect
/// free, so the chain stays unit-testable with no Revit installed. This is the
/// contract the I4 rule resolver will implement; nothing about rule authoring,
/// storage or ordering is specified here, and specifying it before real models
/// have produced real rules is the premature abstraction this change exists to
/// avoid.
/// </para>
/// </remarks>
public interface ICodeResolver
{
    /// <summary>The code this link resolves for the element, or <c>null</c> for no match.</summary>
    string? Resolve(ElementTakeoff element);
}
