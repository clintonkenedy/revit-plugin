namespace Metrado.Domain;

/// <summary>
/// One layer of a compound type, as the type states it: its place, what it
/// does, how thick it is, and its material.
/// </summary>
/// <remarks>
/// Get-only, as <see cref="MaterialRef"/> is: these are extraction facts, and
/// the guards below would have no say over a <c>with</c> copy.
/// </remarks>
public sealed record CompoundLayer
{
    /// <param name="position">Its index in the type, exterior (or top) first.</param>
    /// <param name="function">What it does; null when Revit assigns it none.</param>
    /// <param name="width">Its thickness, in metres; a membrane's is zero.</param>
    /// <param name="materialId">Its material's UniqueId; null when the layer has none.</param>
    /// <param name="materialFromCategory">
    /// The layer states no material and Revit gives it the category's, which
    /// <paramref name="materialId"/> then names.
    /// </param>
    /// <exception cref="ArgumentException">Any of the above cannot be true of a layer.</exception>
    public CompoundLayer(int position, LayerFunction? function, Quantity width, string? materialId, bool materialFromCategory = false)
    {
        if (position < 0)
        {
            throw new ArgumentException("A layer's position must not be negative.", nameof(position));
        }

        if (function is LayerFunction declared && !Enum.IsDefined(typeof(LayerFunction), declared))
        {
            throw new ArgumentException($"{(int)declared} is not a declared layer function.", nameof(function));
        }

        if (width.Unit != QuantityUnit.Metre || !(width.Value >= 0) || double.IsInfinity(width.Value))
        {
            throw new ArgumentException($"A layer's width must be a finite length in metres, not {width}.", nameof(width));
        }

        if (materialId is not null && string.IsNullOrWhiteSpace(materialId))
        {
            throw new ArgumentException("A layer's material, when it has one, must be named.", nameof(materialId));
        }

        if (materialFromCategory && materialId is null)
        {
            throw new ArgumentException("A layer given the category's material must name it.", nameof(materialFromCategory));
        }

        Position = position;
        Function = function;
        Width = width;
        MaterialId = materialId;
        MaterialFromCategory = materialFromCategory;
    }

    public int Position { get; }

    public LayerFunction? Function { get; }

    public Quantity Width { get; }

    public string? MaterialId { get; }

    public bool MaterialFromCategory { get; }
}
