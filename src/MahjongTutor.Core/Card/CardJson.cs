using System.Text.Json;

namespace MahjongTutor.Core.Card;

/// <summary>
/// MahjongCard JSON — the canonical interchange format, also used by the editor's
/// import/export. camelCase; <see cref="Parse"/> validates patterns eagerly.
/// </summary>
public static class CardJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize(MahjongCard card) => JsonSerializer.Serialize(card, Options);

    /// <summary>Deserializes and fully validates a card. Throws <see cref="CardValidationException"/>.</summary>
    public static MahjongCard Parse(string json)
    {
        MahjongCard card;
        try
        {
            card = JsonSerializer.Deserialize<MahjongCard>(json, Options)
                   ?? throw new CardValidationException("empty document");
        }
        catch (JsonException e)
        {
            throw new CardValidationException($"malformed JSON: {e.Message}");
        }
        var errors = Validate(card);
        if (errors.Count > 0)
            throw new CardValidationException(string.Join("; ", errors));
        return card;
    }

    /// <summary>All validation problems with a card (empty = valid).</summary>
    public static List<string> Validate(MahjongCard card)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(card.Name)) errors.Add("card name is empty");
        if (card.Categories.Count == 0) errors.Add("card has no categories");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in card.Categories)
        {
            if (string.IsNullOrWhiteSpace(cat.Name)) errors.Add("a category has no name");
            foreach (var hand in cat.Hands)
            {
                var where = $"{cat.Name}/{hand.Id}";
                if (!ids.Add(hand.Id)) errors.Add($"{where}: duplicate hand id");
                if (hand.Points <= 0) errors.Add($"{where}: points must be positive");
                try
                {
                    if (hand.TileCount != 14)
                        errors.Add($"{where}: pattern has {hand.TileCount} tiles, must be 14");
                }
                catch (PatternParseException e)
                {
                    errors.Add($"{where}: {e.Message}");
                }
            }
        }
        return errors;
    }
}

public sealed class CardValidationException(string message) : FormatException(message);
