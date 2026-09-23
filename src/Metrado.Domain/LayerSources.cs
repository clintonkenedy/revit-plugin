namespace Metrado.Domain;

/// <summary>
/// The source keys a layered element's quantities cross the seam under. The
/// first two are Revit's own material takeoff fields, one entry per material
/// Revit measures; the third is the whole element's volume they reconcile to.
/// </summary>
public static class LayerSources
{
    /// <summary>A material's volume in the element, in m3.</summary>
    public const string MaterialVolume = "MATERIAL_VOLUME";

    /// <summary>A material's area in the element, in m2.</summary>
    public const string MaterialArea = "MATERIAL_AREA";

    /// <summary>The whole element's volume, in m3, which the materials' volumes add up to.</summary>
    public const string HostVolume = "HOST_VOLUME_COMPUTED";
}
