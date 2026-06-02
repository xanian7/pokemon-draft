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

builder.Services.AddDbContext<DraftDbContext>(opts => opts
    .UseSqlServer(connectionString, sqlOpts => sqlOpts
        .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)
        .CommandTimeout(60)));
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddHttpClient<IDiscordService, DiscordService>();
builder.Services.AddHttpClient<IPokemonService, PokemonService>();
builder.Services.AddHttpClient("discord");
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

// Apply any pending EF Core migrations on startup (non-blocking background task).
_ = Task.Run(async () =>
{
    await Task.Delay(500); // brief pause to let the app start first
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DraftDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations.");
    }
});

if (app.Environment.IsDevelopment())
{
    var clientAppDir = Path.Combine(app.Environment.ContentRootPath, "ClientApp");
    var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
    StopProcessOnPort(5173);

    var vite = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "npm",
            Arguments = isWindows ? "/c npm run dev" : "run dev",
            WorkingDirectory = clientAppDir,
            UseShellExecute = isWindows,
            WindowStyle = isWindows ? System.Diagnostics.ProcessWindowStyle.Normal : System.Diagnostics.ProcessWindowStyle.Hidden,
        }
    };
    vite.Start();
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        try
        {
            if (isWindows)
            {
                if (!vite.HasExited) vite.Kill(true);
            }
            else
            {
                // Kill the entire process group on macOS/Linux so child node processes are also killed
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "kill",
                    Arguments = $"-TERM -{vite.Id}",
                    UseShellExecute = false,
                })?.WaitForExit(3000);
                if (!vite.HasExited) vite.Kill(true);
            }
        }
        catch { }

        StopProcessOnPort(5173);
    });
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

static void StopProcessOnPort(int port)
{
    if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
    {
        return;
    }

    try
    {
        using var powershell = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"$conn = Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue; if ($conn) {{ $conn | Select-Object -ExpandProperty OwningProcess -Unique | ForEach-Object {{ Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }} }}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        powershell?.WaitForExit(3000);
    }
    catch { }
}
