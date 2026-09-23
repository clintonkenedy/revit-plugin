using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// The quantity sources extraction reads, for each category it extracts. A
/// criterion naming any other source finds it empty on every element, and the
/// run would then say the model lacks it, which is false: the add-in never
/// looked. Such criteria are refused before the model is read.
/// </summary>
public static class ReadableSources
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByCategory { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [HostTakeoff.WallsKey] = [HostTakeoff.ComputedAreaSource],
        [HostTakeoff.FloorsKey] = [HostTakeoff.ComputedAreaSource],
        [HostTakeoff.RoofsKey] = [HostTakeoff.ComputedAreaSource],
        [OtherElementReader.RailingsKey] = [OtherElementReader.LengthSource],
        [OtherElementReader.DoorsKey] = [],
        [OtherElementReader.WindowsKey] = [],
    };

    /// <summary>
    /// The categories the export takes off by material layer when the criteria
    /// ask. None until the export measures by layer (PR 32): until then such
    /// criteria are refused, never measured whole without a word.
    /// </summary>
    public static IReadOnlySet<string> LayeredCategories { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Null when every source the criteria name for an extracted category is
    /// one extraction reads, and every category they take off by layer is one
    /// the export takes off so.
    /// </summary>
    public static ConfigError? Check(CriteriaSet criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        foreach ((string category, IReadOnlyList<string> readable) in ByCategory)
        {
            if (!criteria.ByCategory.TryGetValue(category, out CategoryCriterion? criterion))
            {
                continue;
            }

            List<string> unreadable = [.. criterion.Sources.Where(source => !readable.Contains(source))];
            if (unreadable.Count > 0)
            {
                return new ConfigError(
                    $"The criteria ask {category} to be measured from {string.Join(", ", unreadable)}, which Metrado does not read for {category}: "
                    + (readable.Count == 0
                        ? "it reads no quantity for them, since they are counted."
                        : $"it reads {string.Join(", ", readable)}."));
            }
        }

        foreach ((string category, CategoryCriterion criterion) in criteria.ByCategory)
        {
            if (criterion.Layers is not null && !LayeredCategories.Contains(category))
            {
                return new ConfigError(
                    $"The criteria ask to take {category} off by material layer, which Metrado does not do yet: "
                    + "leave out its \"layers\", or set it to false.");
            }
        }

        return null;
    }
}
