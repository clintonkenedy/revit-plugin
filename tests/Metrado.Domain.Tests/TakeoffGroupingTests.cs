namespace Metrado.Domain.Tests;

/// <summary>
/// Grouping measured elements into the budget structure: category to capitulo,
/// resolved code to partida, instance to linea de medicion.
/// </summary>
public sealed class TakeoffGroupingTests
{
    [Fact]
    public void TwoWallTypesSharingOnePartidaCodeCollapseIntoASinglePartida()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            .. TakeoffFixture.Instances("WT-A", count: 3, partidaCode: "C1010", amount: 10.0),
            .. TakeoffFixture.Instances("WT-B", count: 2, partidaCode: "C1010", amount: 4.0),
        ]);

        Partida partida = Assert.Single(result.Partidas);

        Assert.Equal(new PartidaKey("Walls", "C1010"), partida.Key);
        Assert.Equal(5, partida.Lineas.Count);
        Assert.Equal(
            5,
            partida
                .Lineas.Select(linea => linea.Element.UniqueId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(38.0, partida.Total.Value, 9);
    }

    [Fact]
    public void TwoDifferentPartidaCodesInOneCapituloStayTwoPartidas()
    {
        TakeoffResult result = TakeoffResult.Group(
            [TakeoffFixture.Line("wall-1", "C1010"), TakeoffFixture.Line("wall-2", "C2020")]);

        Assert.Equal(
            ["C1010", "C2020"],
            result.Partidas.Select(partida => partida.Key.PartidaCode).OrderBy(
                code => code,
                StringComparer.Ordinal));
    }

    [Fact]
    public void TypeIdentityIsNotPartOfTheGroupingKey()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            TakeoffFixture.Line("wall-1", "C1010", typeName: "Generic - 200mm"),
            TakeoffFixture.Line("wall-2", "C1010", typeName: "Generic - 300mm"),
        ]);

        Partida partida = Assert.Single(result.Partidas);

        Assert.Equal(2, partida.Lineas.Count);

        // Asserted so the collapse cannot be credited to two lines that happened to
        // share a type key: they genuinely differ, and the key ignores the difference.
        Assert.Equal(
            ["Basic Wall:Generic - 200mm", "Basic Wall:Generic - 300mm"],
            partida.Lineas.Select(linea => linea.Element.TypeKey));
    }

    [Fact]
    public void OnePartidaCodeUnderTwoCapitulosStaysTwoPartidas()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            TakeoffFixture.Line("wall-1", "C1010", category: "Walls"),
            TakeoffFixture.Line("floor-1", "C1010", category: "Floors"),
        ]);

        Assert.Equal(
            ["Floors", "Walls"],
            result.Partidas.Select(partida => partida.Key.Capitulo).OrderBy(
                capitulo => capitulo,
                StringComparer.Ordinal));
    }

    [Fact]
    public void UnclassifiedElementsAreMeasuredAndReachTheResultAsTheirOwnPartida()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            TakeoffFixture.Line("wall-1", "C1010", amount: 10.0),
            TakeoffFixture.Line("wall-2", UnclassifiedResolver.Code, amount: 6.0),
            TakeoffFixture.Line("wall-3", UnclassifiedResolver.Code, amount: 4.0),
        ]);

        Partida unclassified = PartidaCoded(result, UnclassifiedResolver.Code);

        Assert.True(unclassified.IsUnclassified);
        Assert.False(PartidaCoded(result, "C1010").IsUnclassified);
        Assert.Equal(
            ["wall-2", "wall-3"],
            unclassified.Lineas.Select(linea => linea.Element.UniqueId));
        Assert.Equal(10.0, unclassified.Total.Value, 9);
    }

    [Fact]
    public void AnUnclassifiedLineStillCarriesTheIdentityTheUserNeedsToFixTheModel()
    {
        TakeoffResult result = TakeoffResult.Group(
            [TakeoffFixture.Line("wall-9", UnclassifiedResolver.Code)]);

        Linea linea = Assert.Single(Assert.Single(result.Partidas).Lineas);

        Assert.Equal("wall-9", linea.Element.UniqueId);
        Assert.Equal("Walls", linea.Element.CategoryName);
        Assert.Equal("Basic Wall", linea.Element.FamilyName);
        Assert.Equal("Generic - 200mm", linea.Element.TypeName);
    }

    private static Partida PartidaCoded(TakeoffResult result, string partidaCode) =>
        Assert.Single(
            result.Partidas,
            partida =>
                string.Equals(partida.Key.PartidaCode, partidaCode, StringComparison.Ordinal));
}
