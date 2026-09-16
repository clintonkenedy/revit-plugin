using System.Reflection;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>metrado-measurement</c>, requirement "Per-Category Measurement Criterion":
/// an element whose sources yielded nothing "MUST NOT be silently assigned zero as
/// if it were measured". These tests pin the type that makes that impossible
/// rather than merely discouraged.
/// </summary>
public sealed class SourceSelectionTests
{
    /// <summary>
    /// The selected branch is never reached when no source had a value.
    /// </summary>
    /// <remarks>
    /// The callback throws, so the only way this test can pass is that it was not
    /// invoked at all. That is the structural guarantee: a quantity is not merely
    /// withheld from the caller, it is never produced.
    /// </remarks>
    [Fact]
    public void NoSelectionNeverHandsAQuantityToTheSelectedBranch()
    {
        string reported = SourceSelection.None.Match(
            selected: quantity => throw new Xunit.Sdk.XunitException(
                $"The selected branch ran for an absent selection and was given {quantity}."),
            none: () => "no source");

        Assert.Equal("no source", reported);
    }

    /// <summary>
    /// The companion case: a real selection does reach the selected branch, and
    /// carries the quantity it was built from.
    /// </summary>
    /// <remarks>
    /// Without this, a <c>Match</c> that always took the <c>none</c> branch would
    /// satisfy the test above perfectly while measuring nothing at all.
    /// </remarks>
    [Fact]
    public void ASelectionHandsItsOwnQuantityToTheSelectedBranch()
    {
        SourceSelection selection =
            SourceSelection.Of(new Quantity(18.6, QuantityUnit.SquareMetre));

        string reported = selection.Match(
            selected: quantity => quantity.ToString(),
            none: () => "no source");

        Assert.Equal("18.6 m2", reported);
    }

    /// <summary>
    /// The two states are distinguishable without reading the quantity.
    /// </summary>
    [Fact]
    public void ASelectionReportsWhetherItCarriesAQuantity()
    {
        Assert.True(SourceSelection.Of(new Quantity(0.0, QuantityUnit.SquareMetre)).HasQuantity);
        Assert.False(SourceSelection.None.HasQuantity);
    }

    /// <summary>
    /// Unreachability, part one: <see cref="SourceSelection.Match{T}"/> is the only
    /// route to a <see cref="Quantity"/>.
    /// </summary>
    /// <remarks>
    /// <c>Match</c> returns its own generic parameter, so no public member of this
    /// type has <see cref="Quantity"/> as its result. An absent selection therefore
    /// yields no expression of that type at all — which is what a nullable
    /// <c>Quantity?</c> would not give us, since <c>GetValueOrDefault()</c>
    /// manufactures a 0.0 nobody measured.
    /// </remarks>
    [Fact]
    public void NoPublicMemberOfASelectionYieldsAQuantityOutsideMatch()
    {
        MemberInfo[] surface = typeof(SourceSelection).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly);

        // Proves reflection actually inspected the type rather than an empty set.
        Assert.Contains(nameof(SourceSelection.Match), surface.Select(member => member.Name));

        string[] leaks = surface
            .Where(member => Yields(member, typeof(Quantity))
                || Yields(member, typeof(Quantity?)))
            .Select(member => member.Name)
            .ToArray();

        Assert.True(
            leaks.Length == 0,
            "These members hand out a Quantity without going through Match, so an "
                + "unmeasured element can be read as a measured zero: "
                + string.Join(", ", leaks));
    }

    /// <summary>Whether reading this member produces a value of the given type.</summary>
    private static bool Yields(MemberInfo member, Type type) => member switch
    {
        PropertyInfo property => property.PropertyType == type,
        FieldInfo field => field.FieldType == type,

        // Covers conversion operators too: op_Implicit and op_Explicit are methods,
        // and an implicit conversion to Quantity would be the quietest leak of all.
        MethodInfo method => method.ReturnType == type,
        _ => false,
    };
}
