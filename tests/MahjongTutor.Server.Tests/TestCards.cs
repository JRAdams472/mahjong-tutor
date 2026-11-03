namespace MahjongTutor.Server.Tests;

/// <summary>Minimal valid card for persistence tests — synthetic, never real NMJL data.</summary>
public static class TestCards
{
    public const string Json = """
        {
          "year": 2099,
          "name": "Persist Test Card",
          "categories": [
            { "name": "Run", "hands": [
              { "id": "R1", "points": 25, "pattern": "11a 222a 3333a 444a 55a" },
              { "id": "R2", "points": 30, "concealed": true, "pattern": "FF X0a XX1a XXX2a X0b XX1b XXX2b" }
            ]}
          ]
        }
        """;
}
