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
/// <see cref="ConfigSource"/> — stay in Domain, because <see cref="RunReport"/>
/// reports the provenance and Domain cannot reference this assembly.
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
            found: _ => Result<EffectiveCriteria, ConfigError>.Err(CannotHonourYet(path)),
            absent: BuiltInDefaults,
            unreadable: Result<EffectiveCriteria, ConfigError>.Err);
    }

    /// <summary>
    /// No file was there, so the product's own criteria are in force.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, "Usable Defaults Without Any Configuration":
    /// the add-in "SHALL run correctly with no configuration file present" and
    /// "Absence of configuration MUST NOT be an error". This is the only branch that
    /// returns criteria in I1.
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
    /// A criteria file was supplied and read, and this increment has no reader for
    /// it, so the run stops.
    /// </summary>
    /// <remarks>
    /// The "External, Versionable Criteria File" requirement is tagged I2 and its
    /// reader is task 2.1, so a file arriving here is an input I1 cannot honour.
    /// That leaves one spec-compliant response: "Invalid Configuration Fails Loudly"
    /// states the system "MUST NOT silently fall back to defaults when a file was
    /// supplied, because a silently ignored configuration produces a confidently
    /// wrong budget". The MUST NOT is unconditional — it does not ask <em>why</em>
    /// the file could not be honoured — so returning the defaults here is forbidden
    /// however sympathetic the reason.
    /// <para>
    /// Stopping is modelled as an <see cref="ConfigError"/> rather than thrown:
    /// <c>ExportTakeoffCommand</c> surfaces this type as a blocking dialog, whereas
    /// an exception crossing the Revit boundary reaches the user as a host fault
    /// with no file named in it.
    /// </para>
    /// <para>
    /// <see cref="ConfigError.Location"/> is left null on purpose. Nothing was
    /// parsed, so there is no failing position; a default-constructed location would
    /// report line 0, position 0 and send the estimator hunting a syntax error in a
    /// file that is very likely valid. For the same reason the message blames this
    /// version's capability, not the file's contents.
    /// </para>
    /// </remarks>
    private static ConfigError CannotHonourYet(string? path)
    {
        string subject = path is null ? "A criteria file" : $"The criteria file '{path}'";

        return new ConfigError(
            $"{subject} was supplied, but this version cannot read criteria files yet. "
                + "The run stopped instead of continuing, because measuring under the "
                + "built-in defaults would have discarded the criteria in that file "
                + "without saying so. Remove the file to measure under the built-in "
                + "defaults deliberately.")
        {
            FilePath = path,
        };
    }
}
