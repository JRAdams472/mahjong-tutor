using MahjongTutor.Server.Cards;
using MahjongTutor.Server.Components;
using MahjongTutor.Server.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
builder.Services.AddDbContextFactory<MahjongTutorDbContext>(
    o => o.UseNpgsql(connString));
builder.Services.AddScoped<ICardStore, CardStore>();

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
app.MapGet("/cards/{id:int}/export", async (int id, ICardStore store) =>
{
    var json = await store.ExportJsonAsync(id);
    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
    return Results.File(bytes, "application/json", $"mahjong-card-{id}.json");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
