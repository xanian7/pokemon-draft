using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
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

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (!string.IsNullOrWhiteSpace(jwtSecret))
{
    // Disable ASP.NET Core's default claim type remapping so "sub" stays as "sub"
    JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.MapInboundClaims = false; // keep "sub", "email", etc. as original JWT names
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "pokemon-draft",
                ValidAudience = "pokemon-draft",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            };
        });
    builder.Services.AddAuthorization();
}

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

// Required for Google One Tap / popup sign-in to postMessage back to the opener
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.MapHub<DraftHub>("/hubs/draft");
app.MapControllers();

if (!app.Environment.IsDevelopment())
{
    var spaRoot = Path.Combine(app.Environment.ContentRootPath, "ClientApp", "dist");
    if (Directory.Exists(spaRoot))
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaRoot) });
}

app.Run();
