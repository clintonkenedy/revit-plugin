using System.Globalization;
using System.Text;
using System.Text.Json;
using Metrado.Domain;

namespace Metrado.Configuration;

/// <param name="Override">The fields the entry states, and nothing about the ones it left out.</param>
/// <param name="Location">Where the entry's category name is written, so a later refusal can point at it.</param>
public sealed record LocatedOverride(CategoryOverride Override, ConfigLocation Location);

/// <summary>
/// Reads a criteria file (D5): JSON with comments and trailing commas, one
/// entry per category, each field optional and inherited from the built-in
/// criterion when left out:
/// <code>
/// {
///   // Walls: small openings stay in the metrado
///   "Walls": { "unit": "m2", "sources": ["HOST_AREA_COMPUTED"], "threshold": 1.0, "mode": "exclusive" },
/// }
/// </code>
/// What the file's own shape can tell is refused here, with the line and
/// position where it is: malformed syntax, an entry that is not an object, an
/// unknown or repeated field, a value of the wrong kind, a unit or mode outside
/// its closed set. What only the product's criteria can judge (an unsupported
/// category, a repeated one, a negative threshold) is passed on as written for
/// <see cref="CriteriaSet.Merge"/> to refuse. A field the reader ignored would
/// leave its category on the default without a word, so nothing is ignored.
/// </summary>
public static class CriteriaFile
{
    private const string Unit = "unit";
    private const string Sources = "sources";
    private const string Threshold = "threshold";
    private const string Mode = "mode";

    private const string Layers = "layers";

    private static readonly string[] Fields = [Unit, Sources, Threshold, Mode, Layers];

    private static readonly LayerFunction[] Functions = Enum.GetValues<LayerFunction>();

    private static readonly Dictionary<string, BoundaryMode> Modes = new(StringComparer.Ordinal)
    {
        ["exclusive"] = BoundaryMode.Exclusive,
        ["inclusive"] = BoundaryMode.Inclusive,
    };

    private static readonly JsonReaderOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The entries of a criteria file, in the order written, or why the file cannot be honoured.</summary>
    /// <remarks>Lines and positions count from 1, and positions count characters, as an editor shows them.</remarks>
    public static Result<IReadOnlyList<LocatedOverride>, ConfigError> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        Utf8JsonReader reader = new(utf8, Options);
        try
        {
            return ReadFile(ref reader, utf8);
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<LocatedOverride>, ConfigError>.Err(
                new ConfigError($"The file is not valid JSON: {Reason(ex)}")
                {
                    Location = InLine(utf8, ex.LineNumber ?? 0, ex.BytePositionInLine ?? 0),
                });
        }
        catch (InvalidOperationException)
        {
            // The reader accepts any \uXXXX escape; one standing for half of a
            // surrogate pair fails only when the text is decoded, and must stop
            // the run like any other refusal, never escape to Revit unnamed.
            return Result<IReadOnlyList<LocatedOverride>, ConfigError>.Err(
                new ConfigError("A \\u escape there stands for half of a surrogate pair, which is no character.")
                {
                    Location = At(utf8, reader.TokenStartIndex),
                });
        }
    }

    private static Result<IReadOnlyList<LocatedOverride>, ConfigError> ReadFile(ref Utf8JsonReader reader, byte[] utf8)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return Refuse<IReadOnlyList<LocatedOverride>>(
                At(utf8, reader.TokenStartIndex),
                "The file must be a JSON object with one entry per category, such as { \"Walls\": { \"threshold\": 1.0 } }.");
        }

        List<LocatedOverride> entries = [];
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ConfigLocation where = At(utf8, reader.TokenStartIndex);
            string category = reader.GetString()!;
            if (string.IsNullOrWhiteSpace(category))
            {
                return Refuse<IReadOnlyList<LocatedOverride>>(where, "An entry has no category name.");
            }

            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return Refuse<IReadOnlyList<LocatedOverride>>(
                    At(utf8, reader.TokenStartIndex),
                    $"The entry for '{category}' must be an object of fields, such as {{ \"threshold\": 1.0 }}.",
                    category);
            }

            Result<CategoryOverride, ConfigError> entry = ReadEntry(ref reader, utf8, category);
            if (!entry.IsOk)
            {
                return Result<IReadOnlyList<LocatedOverride>, ConfigError>.Err(entry.Error);
            }

            entries.Add(new LocatedOverride(entry.Value, where));
        }

        // The reader itself refuses anything after the closing brace.
        while (reader.Read())
        {
        }

        return Result<IReadOnlyList<LocatedOverride>, ConfigError>.Ok(entries);
    }

    private static Result<CategoryOverride, ConfigError> ReadEntry(ref Utf8JsonReader reader, byte[] utf8, string category)
    {
        QuantityUnit? unit = null;
        List<string>? sources = null;
        double? threshold = null;
        BoundaryMode? mode = null;
        LayerOverride? layers = null;
        HashSet<string> stated = new(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ConfigLocation fieldAt = At(utf8, reader.TokenStartIndex);
            string field = reader.GetString()!;
            if (!Fields.Contains(field, StringComparer.Ordinal))
            {
                return Refuse<CategoryOverride>(
                    fieldAt,
                    $"'{field}' is not a field of the entry for '{category}'. Accepted fields: {string.Join(", ", Fields)}.",
                    category,
                    field);
            }

            if (!stated.Add(field))
            {
                return Refuse<CategoryOverride>(fieldAt, $"'{field}' is stated twice in the entry for '{category}'. State it once.", category, field);
            }

            reader.Read();
            ConfigLocation valueAt = At(utf8, reader.TokenStartIndex);
            string written = Written(ref reader, utf8);
            switch (field)
            {
                case Unit:
                    QuantityUnit[] units = Enum.GetValues<QuantityUnit>();
                    string? symbol = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    QuantityUnit? named = units.Cast<QuantityUnit?>().FirstOrDefault(candidate => candidate!.Value.Symbol() == symbol);
                    if (named is null)
                    {
                        return Refuse<CategoryOverride>(
                            valueAt,
                            $"'{written}' is not a unit for '{Unit}' in the entry for '{category}'. Accepted units: {string.Join(", ", units.Select(u => u.Symbol()))}.",
                            category,
                            written);
                    }

                    unit = named;
                    break;

                case Mode:
                    if (reader.TokenType != JsonTokenType.String || !Modes.TryGetValue(reader.GetString()!, out BoundaryMode chosen))
                    {
                        return Refuse<CategoryOverride>(
                            valueAt,
                            $"'{written}' is not a boundary mode for '{Mode}' in the entry for '{category}'. Accepted values: {string.Join(", ", Modes.Keys)}.",
                            category,
                            written);
                    }

                    mode = chosen;
                    break;

                case Threshold:
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out double value) || !double.IsFinite(value))
                    {
                        return Refuse<CategoryOverride>(
                            valueAt,
                            $"'{written}' is not a number for '{Threshold}' in the entry for '{category}'.",
                            category,
                            written);
                    }

                    threshold = value;
                    break;

                case Sources:
                    Result<List<string>, ConfigError> listed = ReadSources(ref reader, utf8, category, valueAt);
                    if (!listed.IsOk)
                    {
                        return Result<CategoryOverride, ConfigError>.Err(listed.Error);
                    }

                    sources = listed.Value;
                    break;

                case Layers:
                    Result<LayerOverride, ConfigError> read = ReadLayers(ref reader, utf8, category, valueAt, written);
                    if (!read.IsOk)
                    {
                        return Result<CategoryOverride, ConfigError>.Err(read.Error);
                    }

                    layers = read.Value;
                    break;
            }
        }

        return Result<CategoryOverride, ConfigError>.Ok(new CategoryOverride(category, unit, sources, threshold, mode, layers));
    }

    /// <summary>
    /// A category's material layers: <c>true</c> (every function in m2), an
    /// object of layer functions and units (<c>{}</c> is the same as true), or
    /// <c>false</c>. A function is named as Revit names it; a unit is m2 or m3;
    /// a membrane has no thickness, so it is always m2.
    /// </summary>
    private static Result<LayerOverride, ConfigError> ReadLayers(ref Utf8JsonReader reader, byte[] utf8, string category, ConfigLocation valueAt, string written)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return Result<LayerOverride, ConfigError>.Ok(LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>()));
            case JsonTokenType.False:
                return Result<LayerOverride, ConfigError>.Ok(LayerOverride.Off);
            case JsonTokenType.StartObject:
                break;
            default:
                return Refuse<LayerOverride>(
                    valueAt,
                    $"'{Layers}' in the entry for '{category}' must be true, false or an object of layer functions and units, such as {{ \"Structure\": \"m3\" }}.",
                    category,
                    written);
        }

        Dictionary<LayerFunction, QuantityUnit> units = [];
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ConfigLocation nameAt = At(utf8, reader.TokenStartIndex);
            string name = reader.GetString()!;
            LayerFunction? function = Functions.Cast<LayerFunction?>().FirstOrDefault(candidate => candidate!.Value.ToString() == name);
            if (function is null)
            {
                return Refuse<LayerOverride>(
                    nameAt,
                    $"'{name}' is not a layer function in the layers of '{category}'. Layer functions: {string.Join(", ", Functions)}.",
                    category,
                    name);
            }

            if (units.ContainsKey(function.Value))
            {
                return Refuse<LayerOverride>(nameAt, $"'{name}' is stated twice in the layers of '{category}'. State it once.", category, name);
            }

            reader.Read();
            ConfigLocation unitAt = At(utf8, reader.TokenStartIndex);
            string symbol = Written(ref reader, utf8);
            // Written as the file wrote it: a number or a literal can never spell "m2".
            QuantityUnit? unit = symbol == QuantityUnit.SquareMetre.Symbol() ? QuantityUnit.SquareMetre
                : symbol == QuantityUnit.CubicMetre.Symbol() ? QuantityUnit.CubicMetre
                : null;
            if (unit is null)
            {
                return Refuse<LayerOverride>(
                    unitAt,
                    $"'{symbol}' is not a unit for the {name} layers of '{category}': a layer is measured in m2 or m3.",
                    category,
                    symbol);
            }

            if (function == LayerFunction.Membrane && unit == QuantityUnit.CubicMetre)
            {
                return Refuse<LayerOverride>(
                    unitAt,
                    $"The Membrane layers of '{category}' have no thickness, so their volume is always 0: they are measured in m2.",
                    category,
                    symbol);
            }

            units.Add(function.Value, unit.Value);
        }

        return Result<LayerOverride, ConfigError>.Ok(LayerOverride.On(units));
    }

    /// <summary>An ordered list of source names; an empty list is a choice, never "left out".</summary>
    private static Result<List<string>, ConfigError> ReadSources(ref Utf8JsonReader reader, byte[] utf8, string category, ConfigLocation listAt)
    {
        string must = $"'{Sources}' in the entry for '{category}' must be a list of source names, such as [\"HOST_AREA_COMPUTED\"].";
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return Refuse<List<string>>(listAt, must, category, Written(ref reader, utf8));
        }

        List<string> sources = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String || string.IsNullOrWhiteSpace(reader.GetString()))
            {
                return Refuse<List<string>>(At(utf8, reader.TokenStartIndex), must, category, Written(ref reader, utf8));
            }

            sources.Add(reader.GetString()!);
        }

        return Result<List<string>, ConfigError>.Ok(sources);
    }

    /// <summary>The value as the file wrote it, for naming it in a refusal.</summary>
    /// <summary>
    /// The value as written: a string unquoted, a list or an object whole,
    /// read on a copy so the caller's reader stays where it was.
    /// </summary>
    private static string Written(ref Utf8JsonReader reader, byte[] utf8)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString()!;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            Utf8JsonReader container = reader;
            container.Skip();
            return Encoding.UTF8.GetString(utf8, (int)reader.TokenStartIndex, (int)(container.BytesConsumed - reader.TokenStartIndex));
        }

        return Encoding.UTF8.GetString(utf8, (int)reader.TokenStartIndex, reader.ValueSpan.Length);
    }

    /// <summary>
    /// The 1-based line and character position of a byte offset, as an editor
    /// shows them: an accented letter earlier on the line is one position, not
    /// the two bytes UTF-8 gives it.
    /// </summary>
    private static ConfigLocation At(byte[] utf8, long offset)
    {
        int end = (int)Math.Min(offset, utf8.Length);
        int lineStart = end == 0 ? 0 : Array.LastIndexOf(utf8, (byte)'\n', end - 1) + 1;
        int line = 1 + utf8.AsSpan(0, end).Count((byte)'\n');
        return new ConfigLocation(line, Encoding.UTF8.GetCharCount(utf8, lineStart, end - lineStart) + 1);
    }

    /// <summary>The same, from the reader's own 0-based line and byte position within it.</summary>
    private static ConfigLocation InLine(byte[] utf8, long line, long bytePositionInLine)
    {
        int lineStart = 0;
        for (long seen = 0; seen < line && lineStart < utf8.Length; seen++)
        {
            int newline = Array.IndexOf(utf8, (byte)'\n', lineStart);
            lineStart = newline < 0 ? utf8.Length : newline + 1;
        }

        return At(utf8, Math.Min(utf8.Length, lineStart + bytePositionInLine));
    }

    /// <summary>The reader's reason, without its own 0-based "LineNumber" and "BytePositionInLine".</summary>
    private static string Reason(JsonException ex)
    {
        string message = ex.Message;
        int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return (cut >= 0 ? message[..cut] : message).Trim();
    }

    private static Result<T, ConfigError> Refuse<T>(ConfigLocation where, string message, string? category = null, string? invalid = null) =>
        Result<T, ConfigError>.Err(new ConfigError(message)
        {
            Location = where,
            Category = category,
            InvalidValue = invalid,
        });
}
