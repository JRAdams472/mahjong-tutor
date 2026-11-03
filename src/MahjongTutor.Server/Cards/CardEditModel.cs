using MahjongTutor.Core.Card;

namespace MahjongTutor.Server.Cards;

/// <summary>Mutable view-model backing the card editor (records don't two-way-bind nicely).</summary>
public sealed class CardEditModel
{
    public int Year { get; set; } = DateTime.Today.Year;
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public List<CategoryEditModel> Categories { get; } = [];

    public static CardEditModel FromCard(MahjongCard card)
    {
        var m = new CardEditModel { Year = card.Year, Name = card.Name, Notes = card.Notes };
        foreach (var c in card.Categories)
        {
            var cm = new CategoryEditModel { Name = c.Name };
            foreach (var h in c.Hands)
                cm.Hands.Add(new HandEditModel
                {
                    Id = h.Id, Points = h.Points, Concealed = h.Concealed,
                    Pattern = h.Pattern, Notes = h.Notes,
                });
            m.Categories.Add(cm);
        }
        return m;
    }

    public MahjongCard ToCard() => new()
    {
        Year = Year,
        Name = Name,
        Notes = Notes,
        Categories = Categories.Select(c => new CardCategory
        {
            Name = c.Name,
            Hands = c.Hands.Select(h => new CardHand
            {
                Id = h.Id, Points = h.Points, Concealed = h.Concealed,
                Pattern = h.Pattern.Trim(), Notes = h.Notes,
            }).ToList(),
        }).ToList(),
    };
}

public sealed class CategoryEditModel
{
    public string Name { get; set; } = "";
    public List<HandEditModel> Hands { get; } = [];
}

public sealed class HandEditModel
{
    public string Id { get; set; } = "";
    public int Points { get; set; } = 25;
    public bool Concealed { get; set; }
    public string Pattern { get; set; } = "";
    public string? Notes { get; set; }
}
