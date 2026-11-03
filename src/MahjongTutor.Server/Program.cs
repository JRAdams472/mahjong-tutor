using MahjongTutor.Server.Cards;
using MahjongTutor.Server.Components;
using MahjongTutor.Server.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- logging: console always, Seq when Seq__ServerUrl is set ----
var logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console();
var seqUrl = builder.Configuration["Seq:ServerUrl"];
if (!string.IsNullOrEmpty(seqUrl)) logger = logger.WriteTo.Seq(seqUrl);
builder.Host.UseSerilog(logger.CreateLogger(), dispose: true);

// ---- telemetry: traces + metrics, OTLP when Otel__Endpoint is set ----
var otlp = builder.Configuration["Otel:Endpoint"];
builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation();
        if (!string.IsNullOrEmpty(otlp)) t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation()
         .AddRuntimeInstrumentation();
        if (!string.IsNullOrEmpty(otlp)) m.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    });

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
builder.Services.AddDbContextFactory<MahjongTutorDbContext>(
    o => o.UseNpgsql(connString));
builder.Services.AddScoped<ICardStore, CardStore>();
builder.Services.AddSingleton<MahjongTutor.Server.Tiles.TileSetManager>();
builder.Services.AddScoped<MahjongTutor.Server.Game.GameSession>();
builder.Services.AddSingleton<MahjongTutor.Server.Game.RoomManager>();
builder.Services.AddScoped<MahjongTutor.Server.Game.PlayerSession>();
builder.Services.AddScoped<MahjongTutor.Server.Game.StatsStore>();
builder.Services.AddScoped<MahjongTutor.Server.Game.SavedGameStore>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<MahjongTutor.Server.Game.OllamaCoach>();

var app = builder.Build();

// Apply EF Core migrations on startup — keeps the compose stack self-contained.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MahjongTutorDbContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();

// Card JSON download.
// Piper TTS proxy: GET /speak?text=... → WAV audio (Piper__Url required).
app.MapGet("/speak", async (string text, IHttpClientFactory http, IConfiguration cfg) =>
{
    var url = cfg["Piper:Url"];
    if (string.IsNullOrEmpty(url) || string.IsNullOrWhiteSpace(text)) return Results.NotFound();
    var res = await http.CreateClient().PostAsync(url,
        new StringContent(System.Text.Json.JsonSerializer.Serialize(new { text }),
            System.Text.Encoding.UTF8, "application/json"));
    if (!res.IsSuccessStatusCode) return Results.Problem("piper unreachable");
    return Results.Stream(await res.Content.ReadAsStreamAsync(), "audio/wav");
});

// Serve uploaded tile-set files from the writable tileset dir.
app.MapGet("/tilefile/{set}/{file}",
    (string set, string file, MahjongTutor.Server.Tiles.TileSetManager tiles) =>
    {
        var path = tiles.UserFile(set, file);
        if (path == null || file.Contains('/') || file.Contains("..")) return Results.NotFound();
        var ct = file.EndsWith(".png") ? "image/png" : "image/svg+xml";
        return Results.File(path, ct);
    });

app.MapGet("/cards/{id:int}/export", async (int id, ICardStore store) =>
{
    var json = await store.ExportJsonAsync(id);
    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
    return Results.File(bytes, "application/json", $"mahjong-card-{id}.json");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
