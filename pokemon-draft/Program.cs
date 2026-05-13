using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using PokemonDraft.Data;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<DraftDbContext>(opts => opts.UseSqlServer(connectionString));
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddHttpClient<IPokemonService, PokemonService>();
builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = new List<string> { "http://localhost:5173", "http://localhost:4173" };
        var extra = builder.Configuration["CorsOrigins"];
        if (!string.IsNullOrWhiteSpace(extra))
            origins.AddRange(extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        policy
            .WithOrigins([.. origins])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Apply any pending EF Core migrations on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DraftDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    var clientAppDir = Path.Combine(app.Environment.ContentRootPath, "ClientApp");
    var vite = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c npm run dev",
            WorkingDirectory = clientAppDir,
            UseShellExecute = true,
        }
    };
    vite.Start();
    app.Lifetime.ApplicationStopping.Register(() => { try { vite.Kill(true); } catch { } });
}
else
{
    var spaRoot = Path.Combine(app.Environment.ContentRootPath, "ClientApp", "dist");
    if (Directory.Exists(spaRoot))
    {
        var fileProvider = new PhysicalFileProvider(spaRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    }
}

app.UseCors();
app.MapHub<DraftHub>("/hubs/draft");
app.MapControllers();

if (!app.Environment.IsDevelopment())
{
    var spaRoot = Path.Combine(app.Environment.ContentRootPath, "ClientApp", "dist");
    if (Directory.Exists(spaRoot))
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaRoot) });
}

app.Run();
