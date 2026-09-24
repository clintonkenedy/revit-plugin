using Metrado.Domain;

namespace Metrado.Configuration;

/// <summary>
/// Turns the result of looking for the criteria file into the criteria a run will
/// measure under, or into the error that stops it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This assembly owns resolution</b> (residual finding N4, which recorded that
/// the design's interface listing no longer said which of the four projects owned
/// which function). It has to: resolution ends in reading a criteria file, and the
/// JSON reader lives here rather than in <c>Metrado.Domain</c> because
/// <c>System.Text.Json</c> is in-box on <c>net10.0</c> but a five-package
/// dependency on <c>netstandard2.0</c>, which would falsify decision D1 (see D5).
/// The value types resolution produces — <see cref="EffectiveCriteria"/>,
/// <see cref="ConfigSource"/> — stay in Domain, beside the
/// <see cref="CriteriaFileLookup"/> it consumes, where every layer can name them.
/// </para>
/// <para>
/// Nothing here touches the file system. The locator that produces a
/// <see cref="CriteriaFileLookup"/> is task 1.23 in <c>Metrado.Revit2027</c>, the
/// assembly allowed to fail at I/O, which keeps every branch below testable on
/// macOS.
/// </para>
/// </remarks>
public static class CriteriaResolver
{
    /// <summary>
    /// Resolves the criteria for a run.
    /// </summary>
    /// <param name="lookup">What looking for the criteria file produced.</param>
    /// <param name="path">
    /// The file the caller probed, used to name the file in an error. It is
    /// deliberately not carried into the result when no file is in force — see the
    /// absent branch.
    /// </param>
    /// <returns>
    /// The criteria and their provenance, or the error that stops the run before any
    /// workbook is written.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="lookup"/> is null, which is not a reading of anything.
    /// Treating it as "no file" would turn a defect in the locator into a run that
    /// silently measured under the defaults.
    /// </exception>
    public static Result<EffectiveCriteria, ConfigError> Resolve(
        CriteriaFileLookup lookup,
        string? path)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        return lookup.Match(
            found: text => FromFile(text, path),
            absent: BuiltInDefaults,
            unreadable: Result<EffectiveCriteria, ConfigError>.Err);
    }

    /// <summary>
    /// No file was there, so the product's own criteria are in force.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, "Usable Defaults Without Any Configuration":
    /// the add-in "SHALL run correctly with no configuration file present" and
    /// "Absence of configuration MUST NOT be an error".
    /// <para>
    /// The probed path is discarded rather than reported. A caller knows which path
    /// it looked at and passes it in for error reporting, but no file's contents are
    /// in force, so naming one here would make the completion dialog read "criteria
    /// from criteria.json" for a run that honoured none — and
    /// <see cref="EffectiveCriteria"/> refuses that pairing outright. Where the
    /// add-in looked is a separate fact from what it applied.
    /// </para>
    /// </remarks>
    private static Result<EffectiveCriteria, ConfigError> BuiltInDefaults() =>
        Result<EffectiveCriteria, ConfigError>.Ok(
            new EffectiveCriteria(CriteriaSet.Default, ConfigSource.BuiltInDefaults, path: null));

    /// <summary>
    /// A criteria file was supplied and read: its entries are laid over the
    /// built-in criteria, or the run stops naming the file and the place in it.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, "Invalid Configuration Fails Loudly": the
    /// system "MUST NOT silently fall back to defaults when a file was supplied".
    /// So every refusal, whether the file's shape (<see cref="CriteriaFile"/>) or
    /// the product's criteria (<see cref="CriteriaSet.Merge"/>) found it, stops
    /// the run as a <see cref="ConfigError"/> the command shows before any
    /// workbook is written. A merge refusal is pointed at the entry of the
    /// category it names, the last one when the category is repeated.
    /// <para>
    /// A file read without its path stops too: criteria in force from a file no
    /// one can name are the silent fallback wearing a file.
    /// </para>
    /// </remarks>
    private static Result<EffectiveCriteria, ConfigError> FromFile(string text, string? path)
    {
        if (path is null)
        {
            return Result<EffectiveCriteria, ConfigError>.Err(new ConfigError(
                "A criteria file was read, but not where from, so it cannot be named as the source of the criteria. The run stopped."));
        }

        Result<CriteriaFileContent, ConfigError> parsed = CriteriaFile.Read(text);
        if (!parsed.IsOk)
        {
            return Result<EffectiveCriteria, ConfigError>.Err(InFile(parsed.Error, parsed.Error.Location, path));
        }

        IReadOnlyList<LocatedOverride> entries = parsed.Value.Entries;
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(
            CriteriaSet.Default,
            [.. entries.Select(entry => entry.Override)]);
        if (!merged.IsOk)
        {
            ConfigLocation? where = merged.Error.Location
                ?? entries.LastOrDefault(entry => entry.Override.Category == merged.Error.Category)?.Location;
            return Result<EffectiveCriteria, ConfigError>.Err(InFile(merged.Error, where, path));
        }

        // A configuration picked in Revit is copied here (task 3.7): its name
        // and shared parameter are in force with its criteria.
        return Result<EffectiveCriteria, ConfigError>.Ok(new EffectiveCriteria(merged.Value, ConfigSource.File, path)
        {
            ConfigurationName = parsed.Value.Configuration?.Name,
            SharedParameter = parsed.Value.Configuration?.SharedParameter,
        });
    }

    /// <summary>The refusal as the estimator reads it: the file, the place in it, the category and the value.</summary>
    private static ConfigError InFile(ConfigError error, ConfigLocation? where, string path)
    {
        string detail = error.Message;
        if (error.Category is { } category && !detail.Contains($"'{category}'", StringComparison.Ordinal))
        {
            detail = $"In the entry for '{category}': {detail}";
        }

        if (error.InvalidValue is { } value && !detail.Contains(value, StringComparison.Ordinal))
        {
            detail += $" The value written is {value}.";
        }

        string place = where is null ? string.Empty : $", line {where.Line}, position {where.Position}";
        return new ConfigError($"The criteria file '{path}'{place}: {detail}")
        {
            FilePath = path,
            Location = where,
            Category = error.Category,
            InvalidValue = error.InvalidValue,
        };
    }
}
