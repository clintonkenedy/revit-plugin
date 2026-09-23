using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Metrado.Revit2027;

/// <summary>
/// Reads railings, doors and windows (task 2.6): railings with
/// their length, doors and windows with no quantity (they are counted), each
/// with the codes its type holds. Parameters are read by built-in identifier
/// or, for the nominated shared parameter, by GUID; never by the name the UI
/// shows. A category the model does not contain yields nothing, no error.
/// </summary>
public static class OtherElementReader
{
    public const string RailingsKey = "Railings";
    public const string DoorsKey = "Doors";
    public const string WindowsKey = "Windows";

    /// <summary>The one source read for railings.</summary>
    public const string LengthSource = "CURVE_ELEM_LENGTH";

    public static IReadOnlyList<ElementReading> ReadAll(Document document, Guid? sharedParameter) =>
    [
        .. InForce(new FilteredElementCollector(document).OfClass(typeof(Railing)))
            .OfType<Railing>()
            .Select(railing => Read(document, railing, RailingsKey, [Length(railing)], sharedParameter, ElementTakeoffs.RepeatedOn(Storeys(document, railing)))),
        .. InForce(TopLevel(new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Doors)))
            .Select(door => Read(document, door, DoorsKey, [], sharedParameter)),
        .. InForce(TopLevel(new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Windows)))
            .Select(window => Read(document, window, WindowsKey, [], sharedParameter)),
    ];

    /// <summary>A code by its built-in identifier; null when the element has no such text parameter.</summary>
    internal static string? Text(Element? element, BuiltInParameter id) =>
        element?.get_Parameter(id) is { StorageType: StorageType.String } parameter ? parameter.AsString() : null;

    /// <summary>
    /// The nominated shared parameter's value, instance first and then type,
    /// keyed by its GUID. A parameter the model does not bind reads nothing,
    /// never an error: incoming models are often another discipline's.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> Shared(Element element, Element? type, Guid? sharedParameter)
    {
        if (sharedParameter is not Guid guid || (element.get_Parameter(guid) ?? type?.get_Parameter(guid)) is not Parameter parameter)
        {
            return new Dictionary<string, string?>();
        }

        return new Dictionary<string, string?>
        {
            [guid.ToString("D")] = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString(),
        };
    }

    /// <summary>
    /// Doors or windows placed in the model, not the shared components nested
    /// in them: a leaf or a sidelight is part of its door, and counting it too
    /// would add a door that is not there.
    /// </summary>
    private static IEnumerable<FamilyInstance> TopLevel(FilteredElementCollector collector) =>
        collector.WhereElementIsNotElementType().OfType<FamilyInstance>().Where(instance => instance.SuperComponent is null);

    /// <summary>Primary design option or none, as for walls.</summary>
    private static IEnumerable<Element> InForce(IEnumerable<Element> elements) =>
        elements.Where(element => element.DesignOption is not { IsPrimary: false });

    private static QuantityReading Length(Element railing) =>
        new(LengthSource, QuantityKind.Length,
            railing.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH) is { StorageType: StorageType.Double, HasValue: true } length
                ? length.AsDouble()
                : double.NaN);

    /// <summary>
    /// How many storeys a stair repeats the railing on: the placement levels
    /// of a multistory stair group, or the storeys of a stair whose own top
    /// level spans several.
    /// </summary>
    private static int Storeys(Document document, Railing railing) => document.GetElement(railing.HostId) switch
    {
        Stairs stairs when stairs.MultistoryStairsId != ElementId.InvalidElementId => railing.GetMultistoryStairsPlacementLevels().Count,
        Stairs stairs => stairs.NumberOfStories,
        _ => 1,
    };

    private static ElementReading Read(
        Document document, Element element, string key, IReadOnlyList<QuantityReading> quantities, Guid? sharedParameter, string? condition = null)
    {
        Element? type = document.GetElement(element.GetTypeId());
        return new ElementReading(
            UniqueId: element.UniqueId,
            CategoryKey: key,
            FamilyName: (type as ElementType)?.FamilyName is { Length: > 0 } family ? family : key,
            TypeName: type?.Name is { Length: > 0 } name ? name : "(unnamed type)",
            TypeUniqueId: type?.UniqueId ?? element.UniqueId,
            Codes: new CodesReading(Text(type, BuiltInParameter.ASSEMBLY_CODE), Text(type, BuiltInParameter.KEYNOTE_PARAM), Shared(element, type, sharedParameter)),
            Quantities: quantities,
            Conditions: condition is null ? [] : [condition]);
    }
}
