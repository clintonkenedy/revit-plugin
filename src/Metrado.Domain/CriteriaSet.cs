using System.Collections.ObjectModel;

namespace Metrado.Domain;

/// <summary>
/// The measurement criteria in force for a run, one per supported category.
/// </summary>
/// <remarks>
/// The set is looked up by the category name extraction puts on an element, so the
/// keys are Revit category names and nothing else.
/// <para>
/// Lookup is <see cref="StringComparison.Ordinal"/>. Category names are Revit
/// identifiers with one fixed spelling, and matching loosely would have to decide
/// what a file declaring both <c>Walls</c> and <c>walls</c> means. Ordinal keeps
/// that question from arising: one of them is simply not a supported category, and
/// the configuration is rejected by name rather than half-applied.
/// </para>
/// </remarks>
public sealed record CriteriaSet
{
    private static readonly CriteriaSet BuiltIn = new(
        new Dictionary<string, CategoryCriterion>(StringComparer.Ordinal)
        {
            ["Walls"] = new CategoryCriterion(
                category: "Walls",
                unit: QuantityUnit.SquareMetre,
                sources: ["HOST_AREA_COMPUTED"],
                threshold: BuiltInThreshold(
                    Defaults.AreaThresholdSquareMetres,
                    QuantityUnit.SquareMetre,
                    "Walls")),
        });

    public CriteriaSet(IReadOnlyDictionary<string, CategoryCriterion> byCategory)
    {
        Guard.RequiredValue(byCategory, nameof(byCategory));

        // Copied rather than wrapped: a set built from a dictionary the caller still
        // holds is not a record of what was configured, it is a view of whatever
        // that caller does next.
        Dictionary<string, CategoryCriterion> copy = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, CategoryCriterion> entry in byCategory)
        {
            copy[entry.Key] = entry.Value;
        }

        ByCategory = new ReadOnlyDictionary<string, CategoryCriterion>(copy);
    }

    /// <summary>
    /// The built-in criteria, applied when no configuration file is present.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, requirement "Usable Defaults Without Any
    /// Configuration": the add-in ships these and runs correctly with no file, and
    /// that absence is not an error. I1 defines one category — Walls, measured as
    /// area in m² with the openings correction applied. Task 2.2 extends the table
    /// to six.
    /// </remarks>
    public static CriteriaSet Default => BuiltIn;

    /// <summary>The criterion for each supported category, keyed by category name.</summary>
    public IReadOnlyDictionary<string, CategoryCriterion> ByCategory { get; }

    /// <summary>
    /// Builds a threshold that is part of the product rather than of a file.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="OpeningsThreshold.TryCreate"/> like every other
    /// caller, so the built-in defaults cannot be a configuration the product would
    /// have rejected from a user. If they ever were, the add-in must not start with
    /// them: a default nobody can fix is worse than a loud failure at load.
    /// </remarks>
    private static OpeningsThreshold BuiltInThreshold(
        double value,
        QuantityUnit unit,
        string category)
    {
        Result<OpeningsThreshold, ConfigError> threshold =
            OpeningsThreshold.TryCreate(value, unit, Defaults.Mode, category);

        return threshold.IsOk
            ? threshold.Value
            : throw new InvalidOperationException(
                $"The built-in criterion for {category} is invalid: {threshold.Error.Message}");
    }
}
