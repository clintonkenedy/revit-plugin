using System.Text;
using System.Text.Json;
using Metrado.Domain;

namespace Metrado.Configuration;

/// <summary>
/// A complete configuration an estimator names, saves and reloads (task 3.6):
/// every category's criterion, thresholds, modes and material layers
/// included, and the codification setting, the shared parameter the chain
/// reads third.
/// </summary>
public sealed record SavedConfiguration(string Name, CriteriaSet Criteria, Guid? SharedParameter);

/// <summary>
/// Writes a configuration as a criteria file that names itself, and reads one
/// back through the same parser and merge a criteria file goes through.
/// </summary>
public static class SavedConfigurations
{
    /// <summary>
    /// The configuration as a criteria file with its header: every field of
    /// every category stated, layers too (all seven functions when layered,
    /// false otherwise), so what the file reproduces never depends on the
    /// built-in criteria it is read over, whatever they become.
    /// </summary>
    public static string Write(SavedConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("$configuration");
            writer.WriteString("name", configuration.Name);
            if (configuration.SharedParameter is Guid shared)
            {
                writer.WriteString("sharedParameter", shared.ToString("D"));
            }
            else
            {
                writer.WriteNull("sharedParameter");
            }

            writer.WriteEndObject();

            foreach (CategoryCriterion criterion in configuration.Criteria.ByCategory.Values.OrderBy(criterion => criterion.Category, StringComparer.Ordinal))
            {
                writer.WriteStartObject(criterion.Category);
                writer.WriteString("unit", criterion.Unit.Symbol());
                writer.WriteStartArray("sources");
                foreach (string source in criterion.Sources)
                {
                    writer.WriteStringValue(source);
                }

                writer.WriteEndArray();
                writer.WriteNumber("threshold", criterion.Threshold.Value);
                writer.WriteString("mode", criterion.Threshold.Mode == BoundaryMode.Inclusive ? "inclusive" : "exclusive");
                if (criterion.Layers is LayerCriterion layers)
                {
                    writer.WriteStartObject("layers");
                    foreach (LayerFunction function in Enum.GetValues<LayerFunction>())
                    {
                        writer.WriteString(function.ToString(), layers.UnitOf(function).Symbol());
                    }

                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteBoolean("layers", false);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// A saved configuration read back: its header, which it must have, and
    /// its entries laid over the built-in criteria; or the refusal, at the
    /// place it names.
    /// </summary>
    public static Result<SavedConfiguration, ConfigError> Read(string text)
    {
        Result<CriteriaFileContent, ConfigError> read = CriteriaFile.Read(text);
        if (!read.IsOk)
        {
            return Result<SavedConfiguration, ConfigError>.Err(read.Error);
        }

        if (read.Value.Configuration is not ConfigurationHeader header)
        {
            return Result<SavedConfiguration, ConfigError>.Err(new ConfigError(
                "The file names no configuration: a saved configuration states \"$configuration\" with its \"name\"."));
        }

        IReadOnlyList<LocatedOverride> entries = read.Value.Entries;
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(CriteriaSet.Default, [.. entries.Select(entry => entry.Override)]);
        if (!merged.IsOk)
        {
            return Result<SavedConfiguration, ConfigError>.Err(merged.Error with
            {
                Location = merged.Error.Location ?? entries.LastOrDefault(entry => entry.Override.Category == merged.Error.Category)?.Location,
            });
        }

        return Result<SavedConfiguration, ConfigError>.Ok(new SavedConfiguration(header.Name, merged.Value, header.SharedParameter));
    }
}
