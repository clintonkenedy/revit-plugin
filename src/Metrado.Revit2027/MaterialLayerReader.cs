using Autodesk.Revit.DB;

namespace Metrado.Revit2027;

/// <summary>
/// Reads a layered host's layers (task 3.1): the Revit-bound half of material
/// layer extraction. It only reads, and <see cref="LayerTakeoff"/> decides
/// what crosses the seam.
/// </summary>
public static class MaterialLayerReader
{
    /// <summary>
    /// The host's layers as Revit states and measures them; null when its type
    /// has no compound structure, which is then measured whole.
    /// </summary>
    /// <param name="sharedParameter">The nominated shared parameter's GUID, read on each material.</param>
    public static LayerReading? Read(Document document, HostObject host, Guid? sharedParameter)
    {
        if ((document.GetElement(host.GetTypeId()) as HostObjAttributes)?.GetCompoundStructure() is not CompoundStructure structure)
        {
            return null;
        }

        // A layer with no material of its own is given the category's, as
        // Revit itself does when it measures the element.
        Element? categoryMaterial = host.Category?.Material;

        List<LayerFacts> layers = [.. structure.GetLayers().Select(layer =>
        {
            Element? material = layer.MaterialId == ElementId.InvalidElementId ? categoryMaterial : document.GetElement(layer.MaterialId);
            return new LayerFacts(
                layer.Function.ToString(),
                layer.Width,
                material?.UniqueId,
                layer.MaterialId == ElementId.InvalidElementId && material is not null,
                layer.LayerCapFlag);
        })];

        return new LayerReading(
            host.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED) is { StorageType: StorageType.Double, HasValue: true } volume
                ? volume.AsDouble()
                : null,
            layers,
            [.. host.GetMaterialIds(false).Select(id => document.GetElement(id)).OfType<Material>().Select(material => new MaterialReading(
                material.UniqueId,
                material.Name,
                host.GetMaterialVolume(material.Id),
                host.GetMaterialArea(material.Id, false),
                OtherElementReader.Text(material, BuiltInParameter.KEYNOTE_PARAM),
                OtherElementReader.Shared(material, null, sharedParameter)))],
            [.. host.GetMaterialIds(true).Select(id => document.GetElement(id)).OfType<Material>()
                .Select(paint => new PaintReading(paint.Name, host.GetMaterialArea(paint.Id, true)))],
            new TypeFacts(
                // A door or window type's Wall Closure other than By host (0)
                // overrides its wall type's wrapping at that insert.
                WrapsAtInserts: structure.OpeningWrapping != OpeningWrappingCondition.None
                    || (host is Wall && host.FindInserts(true, false, false, false)
                        .Select(id => document.GetElement(id))
                        .OfType<FamilyInstance>()
                        .Any(insert => insert.Symbol?.get_Parameter(BuiltInParameter.TYPE_WALL_CLOSURE) is { StorageType: StorageType.Integer } closure
                            && closure.AsInteger() != 0)),
                // IsVerticallyCompound is true of every wall type on both
                // samples, breaks or not; a type whose layers change up its
                // height is one that is not vertically homogeneous.
                VerticallyCompound: !structure.IsVerticallyHomogeneous(),
                VariableLayer: structure.VariableLayerIndex >= 0,
                ShapeEdited: host switch
                {
                    Floor floor => floor.GetSlabShapeEditor()?.IsEnabled == true,
                    RoofBase roof => roof.GetSlabShapeEditor()?.IsEnabled == true,
                    _ => false,
                }));
    }
}
