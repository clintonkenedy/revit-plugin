using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Metrado.Revit2027;

/// <summary>
/// Reads the categories other than walls and floors (task 2.6): railings with
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

    public static IReadOnlyList<ElementReading> ReadAll(Document document, Guid? sharedParameter) =>
    [
        .. InForce(new FilteredElementCollector(document).OfClass(typeof(Railing)))
            .Select(railing => Read(document, railing, RailingsKey, [Length(railing)], sharedParameter)),
        .. InForce(new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().OfType<FamilyInstance>())
            .Select(door => Read(document, door, DoorsKey, [], sharedParameter)),
        .. InForce(new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType().OfType<FamilyInstance>())
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

    /// <summary>Primary design option or none, as for walls.</summary>
    private static IEnumerable<Element> InForce(IEnumerable<Element> elements) =>
        elements.Where(element => element.DesignOption is not { IsPrimary: false });

    private static QuantityReading Length(Element railing) =>
        new("CURVE_ELEM_LENGTH", QuantityKind.Length,
            railing.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH) is { StorageType: StorageType.Double, HasValue: true } length
                ? length.AsDouble()
                : double.NaN);

    private static ElementReading Read(Document document, Element element, string key, IReadOnlyList<QuantityReading> quantities, Guid? sharedParameter)
    {
        Element? type = document.GetElement(element.GetTypeId());
        return new ElementReading(
            UniqueId: element.UniqueId,
            CategoryKey: key,
            FamilyName: (type as ElementType)?.FamilyName is { Length: > 0 } family ? family : key,
            TypeName: type?.Name is { Length: > 0 } name ? name : "(unnamed type)",
            TypeUniqueId: type?.UniqueId ?? element.UniqueId,
            Codes: new CodesReading(Text(type, BuiltInParameter.ASSEMBLY_CODE), Text(type, BuiltInParameter.KEYNOTE_PARAM), Shared(element, type, sharedParameter)),
            Quantities: quantities);
    }
}
