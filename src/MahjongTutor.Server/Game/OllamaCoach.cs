using System.Text;
using System.Text.Json;

namespace MahjongTutor.Server.Game;

/// <summary>Natural-language coach explanations via a local Ollama.</summary>
public sealed class OllamaCoach(IHttpClientFactory http, IConfiguration cfg)
{
    private readonly string? _url = cfg["Ollama:Url"];
    private readonly string _model = cfg["Ollama:Model"] ?? "llama3.2";

    public bool Enabled => !string.IsNullOrEmpty(_url);

    /// <summary>Ask the LLM to explain the current situation in words.</summary>
    public async Task<string> AskAsync(string context, string question)
    {
        if (!Enabled) return "Ollama isn't configured (set Ollama__Url).";
        var prompt =
            "You are a friendly American Mahjong coach. In 2-4 sentences, explain " +
            "the situation and answer the question. Be concrete about the card hand " +
            "and tiles involved.\n\nSituation:\n" + context +
            "\n\nQuestion: " + question;
        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            prompt,
            stream = false,
        });
        try
        {
            var res = await http.CreateClient().PostAsync($"{_url}/api/generate",
                new StringContent(body, Encoding.UTF8, "application/json"));
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("response").GetString()?.Trim()
                ?? "(empty response)";
        }
        catch (Exception ex)
        {
            return $"Ollama unreachable: {ex.Message}";
        }
    }
}
