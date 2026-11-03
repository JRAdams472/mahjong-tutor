using MahjongTutor.Core.Card;

namespace MahjongTutor.Core.Tests;

public class CardJsonTests
{
    [Fact]
    public void Roundtrip_preserves_card()
    {
        var json = CardJson.Serialize(TestCards.Card);
        var back = CardJson.Parse(json);
        Assert.Equal(TestCards.Card.Year, back.Year);
        Assert.Equal(TestCards.Card.Name, back.Name);
        Assert.Equal(
            TestCards.Card.AllHands.Select(h => (h.Id, h.Pattern, h.Points, h.Concealed)),
            back.AllHands.Select(h => (h.Id, h.Pattern, h.Points, h.Concealed)));
    }

    [Fact]
    public void Parse_rejects_malformed_json() =>
        Assert.Throws<CardValidationException>(() => CardJson.Parse("{ not json"));

    [Fact]
    public void Validation_rejects_wrong_tile_count()
    {
        const string json = """
            { "year": 2099, "name": "Bad", "categories": [
              { "name": "X", "hands": [
                { "id": "H1", "points": 25, "pattern": "FF 1111a 2222a" } ] } ] }
            """;
        var ex = Assert.Throws<CardValidationException>(() => CardJson.Parse(json));
        Assert.Contains("must be 14", ex.Message);
    }

    [Fact]
    public void Validation_rejects_duplicate_ids_and_bad_pattern()
    {
        const string json = """
            { "year": 2099, "name": "Bad", "categories": [
              { "name": "X", "hands": [
                { "id": "H1", "points": 25, "pattern": "FF 1111a 2222a 3333a 44a" },
                { "id": "H1", "points": 25, "pattern": "QQQQ" } ] } ] }
            """;
        var ex = Assert.Throws<CardValidationException>(() => CardJson.Parse(json));
        Assert.Contains("duplicate hand id", ex.Message);
    }

    [Fact]
    public void Validate_reports_parse_errors_per_hand()
    {
        var card = new MahjongCard
        {
            Year = 2099, Name = "T",
            Categories = [new CardCategory
            {
                Name = "C",
                Hands = [new CardHand { Id = "H1", Points = 25, Pattern = "D" }]
            }]
        };
        var errors = CardJson.Validate(card);
        Assert.Contains(errors, e => e.Contains("dragon"));
    }
}
