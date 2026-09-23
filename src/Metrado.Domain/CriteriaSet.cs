using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

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
    // Areas for the hosts openings cut; linear metres for railings, as a
    // metrado states them; a count for doors and windows, which read no
    // quantity source (the empty list, N1). Nothing is added back where
    // there are no openings, so those thresholds are zero.
    private static readonly CriteriaSet BuiltIn = new(
        new Dictionary<string, CategoryCriterion>(StringComparer.Ordinal)
        {
            ["Walls"] = BuiltInCriterion("Walls", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"], Defaults.AreaThresholdSquareMetres),
            ["Floors"] = BuiltInCriterion("Floors", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"], Defaults.AreaThresholdSquareMetres),
            ["Roofs"] = BuiltInCriterion("Roofs", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"], Defaults.AreaThresholdSquareMetres),
            ["Railings"] = BuiltInCriterion("Railings", QuantityUnit.Metre, ["CURVE_ELEM_LENGTH"], 0),
            ["Doors"] = BuiltInCriterion("Doors", QuantityUnit.Each, [], 0),
            ["Windows"] = BuiltInCriterion("Windows", QuantityUnit.Each, [], 0),
        });

    public CriteriaSet(IReadOnlyDictionary<string, CategoryCriterion> byCategory)
    {
        Guard.RequiredValue(byCategory, nameof(byCategory));

        ByCategory = new ReadOnlyDictionary<string, CategoryCriterion>(CopyOrdinal(byCategory));
    }

    /// <summary>
    /// The built-in criteria, applied when no configuration file is present.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, requirement "Usable Defaults Without Any
    /// Configuration": the add-in ships these and runs correctly with no file, and
    /// that absence is not an error. They cover the six categories the add-in
    /// measures (task 2.2; extraction reads floors and roofs from task 2.6's second half): walls, floors and roofs by area in m² with the openings
    /// correction, railings by length in m, doors and windows counted in u.
    /// </remarks>
    public static CriteriaSet Default => BuiltIn;

    /// <summary>The criterion for each supported category, keyed by category name.</summary>
    public IReadOnlyDictionary<string, CategoryCriterion> ByCategory { get; }

    /// <summary>
    /// Applies a configuration file's entries over the built-in criteria: per
    /// category, then per field.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, requirement "External, Versionable Criteria
    /// File": "Values present in the file SHALL override the built-in defaults per
    /// category; categories absent from the file SHALL keep their defaults." A
    /// category the overrides never mention is carried through untouched, and a
    /// category they do mention inherits every field it left null.
    /// <para>
    /// Names are validated in full before any field is coalesced, so an entry the
    /// product does not support can never be written into the set as a new
    /// category. The user is told which name was not recognised rather than being
    /// sent to fix a value inside an entry that should not exist.
    /// </para>
    /// </remarks>
    public static Result<CriteriaSet, ConfigError> Merge(
        CriteriaSet defaults,
        IReadOnlyList<CategoryOverride> overrides)
    {
        Guard.RequiredValue(defaults, nameof(defaults));
        Guard.RequiredValue(overrides, nameof(overrides));

        ConfigError? badName = FindBadName(defaults, overrides);
        if (badName is not null)
        {
            return Result<CriteriaSet, ConfigError>.Err(badName);
        }

        Dictionary<string, CategoryCriterion> merged = CopyOrdinal(defaults.ByCategory);

        foreach (CategoryOverride entry in overrides)
        {
            Result<CategoryCriterion, ConfigError> coalesced =
                Coalesce(defaults.ByCategory[entry.Category], entry);

            if (!coalesced.IsOk)
            {
                return Result<CriteriaSet, ConfigError>.Err(coalesced.Error);
            }

            merged[entry.Category] = coalesced.Value;
        }

        return Result<CriteriaSet, ConfigError>.Ok(new CriteriaSet(merged));
    }

    /// <summary>
    /// Returns the first naming fault among the overrides, or null when every entry
    /// names a supported category exactly once.
    /// </summary>
    /// <remarks>
    /// A repeated category is refused rather than resolved. The entries can
    /// disagree, and every silent answer is indefensible: last-wins hides the first,
    /// first-wins hides the last, and coalescing both against the default makes the
    /// outcome depend on which fields each entry happened to set.
    /// </remarks>
    private static ConfigError? FindBadName(
        CriteriaSet defaults,
        IReadOnlyList<CategoryOverride> overrides)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (CategoryOverride entry in overrides)
        {
            if (!defaults.ByCategory.ContainsKey(entry.Category))
            {
                string supported = string.Join(
                    ", ",
                    defaults.ByCategory.Keys.OrderBy(name => name, StringComparer.Ordinal));

                return Rejected(
                    $"'{entry.Category}' is not a category this add-in measures. "
                        + $"Supported categories: {supported}.",
                    entry.Category,
                    entry.Category);
            }

            if (!seen.Add(entry.Category))
            {
                return Rejected(
                    $"Category '{entry.Category}' is configured more than once. "
                        + "Keep exactly one entry per category.",
                    entry.Category,
                    entry.Category);
            }
        }

        return null;
    }

    /// <summary>
    /// Copies the criteria into a fresh ordinal-keyed dictionary.
    /// </summary>
    /// <remarks>
    /// Copied rather than wrapped, on both the constructor's path and the merge's:
    /// a set built over a dictionary its caller still holds is not a record of what
    /// was configured, it is a view of whatever that caller does next.
    /// </remarks>
    private static Dictionary<string, CategoryCriterion> CopyOrdinal(
        IReadOnlyDictionary<string, CategoryCriterion> source)
    {
        Dictionary<string, CategoryCriterion> copy = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, CategoryCriterion> entry in source)
        {
            copy[entry.Key] = entry.Value;
        }

        return copy;
    }

    /// <summary>
    /// Builds one category's effective criterion from its default and the fields
    /// the file stated.
    /// </summary>
    /// <remarks>
    /// The threshold is rebuilt in the <em>resolved</em> unit, not in the unit it
    /// was inherited under. <c>takeoff-configuration</c> requires the threshold to
    /// be "expressed in that category's measurement unit", and
    /// <c>Measurement.Apply</c> refuses a comparison across unit systems — so a file
    /// naming only a new unit would otherwise leave every element in the category
    /// reporting <c>UnitMismatch</c>.
    /// </remarks>
    private static Result<CategoryCriterion, ConfigError> Coalesce(
        CategoryCriterion baseline,
        CategoryOverride entry)
    {
        QuantityUnit unit = entry.Unit ?? baseline.Unit;

        if (!Enum.IsDefined(typeof(QuantityUnit), unit))
        {
            return Result<CategoryCriterion, ConfigError>.Err(
                new ConfigError(
                    $"'{(int)unit}' is not a measurement unit this add-in supports.")
                {
                    Category = entry.Category,
                    InvalidValue = ((int)unit).ToString(CultureInfo.InvariantCulture),
                });
        }

        Result<OpeningsThreshold, ConfigError> threshold = OpeningsThreshold.TryCreate(
            entry.Threshold ?? baseline.Threshold.Value,
            unit,
            entry.Mode ?? baseline.Threshold.Mode,
            entry.Category);

        if (!threshold.IsOk)
        {
            return Result<CategoryCriterion, ConfigError>.Err(threshold.Error);
        }

        IReadOnlyList<string> sources = entry.Sources ?? baseline.Sources;
        if (sources.Count == 0 && unit != QuantityUnit.Each)
        {
            return Result<CategoryCriterion, ConfigError>.Err(Rejected(
                $"'{entry.Category}' lists no quantity source, so it is counted, and a count is in "
                    + $"{QuantityUnit.Each.Symbol()}, not {unit.Symbol()}. Give it a source to measure it in {unit.Symbol()}, or the unit {QuantityUnit.Each.Symbol()}.",
                entry.Category,
                unit.Symbol()));
        }

        Result<LayerCriterion?, ConfigError> layers = entry.Layers is null
            ? Result<LayerCriterion?, ConfigError>.Ok(baseline.Layers)
            : entry.Layers.Match(
                off: () => Result<LayerCriterion?, ConfigError>.Ok(null),
                on: units => Widen(LayerCriterion.TryCreate(units, entry.Category)));

        if (!layers.IsOk)
        {
            return Result<CategoryCriterion, ConfigError>.Err(layers.Error);
        }

        if (layers.Value is not null && (unit != QuantityUnit.SquareMetre || sources.Count == 0))
        {
            return Result<CategoryCriterion, ConfigError>.Err(Rejected(
                $"'{entry.Category}' cannot be taken off by material layer: a layered category is measured in m2 from a quantity source, "
                    + "since its openings and threshold are areas.",
                entry.Category,
                "layers"));
        }

        return Result<CategoryCriterion, ConfigError>.Ok(
            new CategoryCriterion(
                entry.Category,
                unit,
                sources,
                threshold.Value,
                layers.Value));
    }

    private static Result<LayerCriterion?, ConfigError> Widen(Result<LayerCriterion, ConfigError> created) =>
        created.IsOk
            ? Result<LayerCriterion?, ConfigError>.Ok(created.Value)
            : Result<LayerCriterion?, ConfigError>.Err(created.Error);

    private static ConfigError Rejected(string message, string category, string invalidValue) =>
        new ConfigError(message) { Category = category, InvalidValue = invalidValue };

    /// <summary>
    /// Builds a criterion that is part of the product rather than of a file.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="OpeningsThreshold.TryCreate"/> like every other
    /// caller, so the built-in defaults cannot be a configuration the product would
    /// have rejected from a user. If they ever were, the add-in must not start with
    /// them: a default nobody can fix is worse than a loud failure at load.
    /// </remarks>
    private static CategoryCriterion BuiltInCriterion(
        string category,
        QuantityUnit unit,
        IReadOnlyList<string> sources,
        double threshold)
    {
        Result<OpeningsThreshold, ConfigError> built =
            OpeningsThreshold.TryCreate(threshold, unit, Defaults.Mode, category);

        return built.IsOk
            ? new CategoryCriterion(category, unit, sources, built.Value)
            : throw new InvalidOperationException(
                $"The built-in criterion for {category} is invalid: {built.Error.Message}");
    }
}
