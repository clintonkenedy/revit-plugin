namespace Metrado.Domain;

/// <summary>
/// An advisory finding about a modelled element. Warnings never stop a run —
/// only a <see cref="ConfigError"/> does — because the warnings themselves are
/// the user's instruction list for fixing the model.
/// </summary>
/// <remarks>
/// Every field is required. An unattributable warning is noise: the user cannot
/// find the element to fix, and a warning with no condition states nothing.
/// </remarks>
public sealed record ValidationWarning
{
    public ValidationWarning(
        string uniqueId,
        string categoryName,
        string familyName,
        string typeName,
        string condition)
    {
        UniqueId = Guard.RequiredText(uniqueId, nameof(uniqueId));
        CategoryName = Guard.RequiredText(categoryName, nameof(categoryName));
        FamilyName = Guard.RequiredText(familyName, nameof(familyName));
        TypeName = Guard.RequiredText(typeName, nameof(typeName));
        Condition = Guard.RequiredText(condition, nameof(condition));
    }

    public string UniqueId { get; }

    public string CategoryName { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    /// <summary>The condition detected, stated so the user knows what to change.</summary>
    public string Condition { get; }

    /// <summary>
    /// Builds a warning from the element it describes. Preferred over the
    /// constructor at every call site: it is what stops the four identity fields
    /// from drifting apart from the element they are supposed to name.
    /// </summary>
    public static ValidationWarning ForElement(ElementTakeoff element, string condition) =>
        new(
            Guard.RequiredValue(element, nameof(element)).UniqueId,
            element.CategoryName,
            element.FamilyName,
            element.TypeName,
            condition);
}
