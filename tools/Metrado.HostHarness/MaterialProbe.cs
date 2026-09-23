using Autodesk.Revit.DB;

namespace Metrado.HostHarness;

/// <summary>
/// Measures, on a real model, what Revit gives per material for layered
/// walls, floors and roofs (task 3.1), before any rule is written for it:
/// each material's volume and area, the type's layers with their widths and
/// functions, the material's own codes, and the element's computed volume and
/// area to reconcile them against. It only reads, and opens no transaction.
/// </summary>
public static class MaterialProbe
{
    public sealed record Result(List<ElementResult> Elements, bool ModifiedAfterProbe);

    /// <param name="Layers">The type's layers, exterior (or top) first.</param>
    public sealed record ElementResult(
        string Category, string UniqueId, string TypeName, double? VolumeM3, double? AreaM2,
        List<LayerResult> Layers, List<MaterialResult> Materials, List<MaterialResult> Paint);

    public sealed record LayerResult(string Function, double WidthM, string? Material, bool IsCore);

    /// <param name="Keynote">The material's KEYNOTE_PARAM.</param>
    /// <param name="Mark">The material's ALL_MODEL_MARK.</param>
    public sealed record MaterialResult(string Name, string UniqueId, double VolumeM3, double AreaM2, string? Keynote, string? Mark, string? Class);

    public static Result Run(Document document, int maxPerCategory)
    {
        List<ElementResult> results = [];
        foreach (BuiltInCategory category in new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs })
        {
            results.AddRange(new FilteredElementCollector(document)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .OfType<HostObject>()
                .Where(host => Structure(document, host) is { } structure && structure.GetLayers().Count > 1)
                .Take(maxPerCategory)
                .Select(host => Probe(document, host)));
        }

        return new Result(results, document.IsModified);
    }

    private static CompoundStructure? Structure(Document document, HostObject host) =>
        (document.GetElement(host.GetTypeId()) as HostObjAttributes)?.GetCompoundStructure();

    private static ElementResult Probe(Document document, HostObject host)
    {
        CompoundStructure structure = Structure(document, host)!;
        List<CompoundStructureLayer> layers = [.. structure.GetLayers()];
        return new ElementResult(
            host.Category?.BuiltInCategory.ToString() ?? "?",
            host.UniqueId,
            document.GetElement(host.GetTypeId())?.Name ?? "?",
            Read(host, BuiltInParameter.HOST_VOLUME_COMPUTED, UnitTypeId.CubicMeters),
            Read(host, BuiltInParameter.HOST_AREA_COMPUTED, UnitTypeId.SquareMeters),
            [.. layers.Select((layer, index) => new LayerResult(
                layer.Function.ToString(),
                UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Meters),
                document.GetElement(layer.MaterialId)?.Name,
                index >= structure.GetFirstCoreLayerIndex() && index <= structure.GetLastCoreLayerIndex()))],
            [.. host.GetMaterialIds(false).Select(id => Material(document, host, id, paint: false))],
            [.. host.GetMaterialIds(true).Select(id => Material(document, host, id, paint: true))]);
    }

    private static MaterialResult Material(Document document, Element host, ElementId id, bool paint)
    {
        Element? material = document.GetElement(id);
        return new MaterialResult(
            material?.Name ?? id.ToString(),
            material?.UniqueId ?? id.ToString(),
            paint ? 0 : UnitUtils.ConvertFromInternalUnits(host.GetMaterialVolume(id), UnitTypeId.CubicMeters),
            UnitUtils.ConvertFromInternalUnits(host.GetMaterialArea(id, paint), UnitTypeId.SquareMeters),
            material?.get_Parameter(BuiltInParameter.KEYNOTE_PARAM)?.AsString(),
            material?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(),
            (material as Material)?.MaterialClass);
    }

    private static double? Read(Element element, BuiltInParameter id, ForgeTypeId unit) =>
        element.get_Parameter(id) is { HasValue: true, StorageType: StorageType.Double } parameter
            ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), unit)
            : null;
}
