namespace Metrado.Revit2027;

public enum SmokeStatus
{
    Pass = 1,
    Fail = 2,

    /// <summary>The model offered nothing to check; neither a pass nor a failure.</summary>
    Skip = 3,
}

/// <param name="Name">A short, stable name, so runs can be compared line by line.</param>
/// <param name="Detail">What was seen, in enough detail to judge the verdict without Revit.</param>
public sealed record SmokeCheck(string Name, SmokeStatus Status, string Detail);

/// <summary>
/// How each host smoke check judges what it saw. The smoke command gathers
/// the facts inside Revit; these rules decide pass, fail or skip, and say
/// why. Uses no Revit type, so every verdict rule is pinned by a test.
/// </summary>
public static class SmokeChecks
{
    /// <summary>
    /// The add-in runs in a load context of its own (D3): not Revit's default
    /// one, and not one it shares. Revit merges add-ins that declare the same
    /// context name into one context without a word; an assembly in it from
    /// outside the add-in's folder is the sign.
    /// </summary>
    /// <param name="foreign">The locations of assemblies in the context that come from outside the add-in's folder.</param>
    public static SmokeCheck Isolation(string? contextName, bool isDefaultContext, IReadOnlyCollection<string> foreign)
    {
        ArgumentNullException.ThrowIfNull(foreign);

        if (isDefaultContext)
        {
            return new("isolation", SmokeStatus.Fail, $"Metrado.Revit2027 is loaded in context '{contextName}', Revit's default one.");
        }

        return foreign.Count == 0
            ? new("isolation", SmokeStatus.Pass, $"Metrado.Revit2027 is loaded in context '{contextName}', which holds nothing from outside its folder.")
            : new("isolation", SmokeStatus.Fail, $"Context '{contextName}' is shared: it also holds {string.Join(", ", foreign)}.");
    }

    /// <summary>
    /// Every required assembly resolves, from the add-in's own folder. Revit
    /// carries assemblies of the same names; one resolved from anywhere else
    /// means isolation did not hold for it, whatever the files on disk (D7).
    /// </summary>
    /// <param name="resolvedFrom">Each required assembly's location as loaded in the add-in's context; null when it could not be loaded.</param>
    public static SmokeCheck Closure(string folder, IReadOnlyDictionary<string, string?> resolvedFrom)
    {
        ArgumentNullException.ThrowIfNull(resolvedFrom);

        List<string> problems = [];
        foreach (string name in DependencyClosure.RequiredAssemblies)
        {
            string? location = resolvedFrom.GetValueOrDefault(name);
            if (location is null)
            {
                problems.Add($"{name} could not be loaded");
            }
            else if (!string.Equals(Path.GetDirectoryName(location), folder, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{name} from {location}");
            }
        }

        return problems.Count == 0
            ? new("closure", SmokeStatus.Pass, $"All {DependencyClosure.RequiredAssemblies.Count} required assemblies resolved from {folder}.")
            : new("closure", SmokeStatus.Fail, $"Not resolved from {folder}: {string.Join("; ", problems)}.");
    }

    /// <summary>
    /// A wall type's code is read by its built-in parameter, never by the
    /// name the UI shows, which a Spanish UI turns into "Código de montaje".
    /// </summary>
    /// <param name="language">The UI language Revit is running in.</param>
    /// <param name="shownAs">The parameter's name as the UI shows it.</param>
    /// <param name="codedTypes">
    /// How many types of the walls extraction reads carry a code, counted
    /// apart from extraction: the fact a broken reading cannot hide.
    /// </param>
    /// <param name="typeName">The first wall type extraction read a code from; null when it read none.</param>
    public static SmokeCheck AssemblyCode(string language, string? shownAs, int codedTypes, string? typeName, string? code)
    {
        bool read = typeName is not null && !string.IsNullOrWhiteSpace(code);
        if (read)
        {
            return new("assembly-code", SmokeStatus.Pass, $"Extraction read '{code}' from '{typeName}'; the {language} UI shows the parameter as '{shownAs}'.");
        }

        return codedTypes == 0
            ? new("assembly-code", SmokeStatus.Skip, $"No type of the walls extraction reads carries an assembly code (UI language {language}, parameter shown as '{shownAs}').")
            : new("assembly-code", SmokeStatus.Fail, $"{codedTypes} wall types carry an assembly code, and extraction read none (UI language {language}, parameter shown as '{shownAs}').");
    }

    /// <summary>
    /// Every opening cutting the wall is reported on its own, measured or
    /// not: a reading that merged two windows would still carry a total.
    /// </summary>
    /// <param name="wallUniqueId">A wall with one door and two windows; null when the model has none.</param>
    /// <param name="inserts">
    /// The UniqueIds of the door and two windows that cut the wall, as Revit's
    /// own geometry shows (each generates faces of the wall). A hosted insert
    /// that cuts nothing is no opening, and extraction rightly leaves it out.
    /// </param>
    /// <param name="reported">The UniqueIds the reading reported, measured or unmeasured.</param>
    public static SmokeCheck Openings(string? wallUniqueId, IReadOnlyCollection<string> inserts, IReadOnlyCollection<string> reported)
    {
        ArgumentNullException.ThrowIfNull(inserts);
        ArgumentNullException.ThrowIfNull(reported);

        if (wallUniqueId is null)
        {
            return new("openings", SmokeStatus.Skip, "No wall in this model hosts exactly one door and two windows.");
        }

        string[] missing = [.. inserts.Where(insert => !reported.Contains(insert))];
        return missing.Length == 0 && reported.Count == reported.Distinct().Count()
            ? new("openings", SmokeStatus.Pass, $"Wall {wallUniqueId}: its door and two windows are each reported on their own ({string.Join(", ", reported)}).")
            : new("openings", SmokeStatus.Fail, $"Wall {wallUniqueId}: hosts {string.Join(", ", inserts)} but the reading listed {string.Join(", ", reported)}{(missing.Length > 0 ? $"; missing {string.Join(", ", missing)}" : string.Empty)}.");
    }

    /// <summary>
    /// The document comes out as unmodified as it went in. One that already
    /// had unsaved changes cannot show a change of the run's own, so it is
    /// skipped rather than passed.
    /// </summary>
    public static SmokeCheck Unmodified(bool modifiedBefore, bool modifiedAfter) =>
        (modifiedBefore, modifiedAfter) switch
        {
            (true, _) => new("unmodified", SmokeStatus.Skip, "The document already had unsaved changes, so the run's own cannot be told apart."),
            (false, false) => new("unmodified", SmokeStatus.Pass, "The document had no unsaved changes before or after."),
            (false, true) => new("unmodified", SmokeStatus.Fail, "The document had unsaved changes after the run and none before."),
        };
}
