using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BC = BCrypt.Net.BCrypt;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Models;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    DraftDbContext db,
    IConfiguration config,
    ILeagueService leagueService,
    IHttpClientFactory httpClientFactory,
    ILogger<AuthController> logger) : ControllerBase
{
    private string IssueJwt(AppUser user)
    {
        var secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Name),
            new Claim("picture", user.PictureUrl),
        };

        var token = new JwtSecurityToken(
            issuer: "pokemon-draft",
            audience: "pokemon-draft",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(90),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthRequest req)
    {
        var clientId = config["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(503, "Google authentication is not configured.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId]
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken, settings);
        }
        catch
        {
            return Unauthorized("Invalid Google token.");
        }

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.GoogleId == payload.Subject);
        if (user is null)
        {
            user = new AppUser
            {
                GoogleId = payload.Subject,
                Email = payload.Email ?? string.Empty,
                Name = payload.Name ?? payload.Email ?? string.Empty,
                PictureUrl = payload.Picture ?? string.Empty,
            };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
        }
        else
        {
            // Keep profile info fresh
            user.Name = payload.Name ?? user.Name;
            user.PictureUrl = payload.Picture ?? user.PictureUrl;
            await db.SaveChangesAsync();
        }

        var token = IssueJwt(user);
        return Ok(new AuthTokenResponse(token, new AuthUserResponse(user.Id, user.Email, user.Name, user.PictureUrl)));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null) return NotFound();
        return Ok(new AuthUserResponse(user.Id, user.Email, user.Name, user.PictureUrl));
    }

    [HttpGet("my-leagues")]
    [Authorize]
    public async Task<IActionResult> MyLeagues()
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var players = await db.Players
            .Include(p => p.League)
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var result = players.Select(p => new MyLeagueResponse(
            p.LeagueCode,
            p.League.Name,
            p.Id,
            p.Name,
            p.TeamName,
            p.TeamImageUrl,
            p.League.CommissionerPlayerId == p.Id,
            p.IsCoCommissioner
        )).ToList();

        return Ok(result);
    }

    /// <summary>Links a Google-authenticated user to their existing player in a league via PIN.</summary>
    [HttpPost("link-player")]
    [Authorize]
    public async Task<IActionResult> LinkPlayer([FromBody] LinkPlayerRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var code = req.LeagueCode.Trim().ToUpperInvariant();
        var player = await db.Players
            .FirstOrDefaultAsync(p => p.LeagueCode == code && p.Pin == req.Pin);

        if (player is null) return Unauthorized("Invalid league code or PIN.");
        if (player.UserId is not null && player.UserId != userId)
            return Conflict("This player slot is already linked to a different account.");

        player.UserId = userId;
        await db.SaveChangesAsync();

        return Ok(new { player.Id, player.Name, player.TeamName, player.TeamImageUrl });
    }

    /// <summary>Validates a PIN and returns session info. Works for both admin PINs and player PINs.</summary>
    [HttpPost("join")]
    public IActionResult Join(JoinRequest req)
    {
        var result = leagueService.ValidatePin(req.LeagueCode, req.Pin);
        return result is null ? Unauthorized() : Ok(result);
    }

    /// <summary>Returns the session info for a Google user's existing player in a league.
    /// Rotates the player's session token on each call for better security.</summary>
    [HttpPost("enter-league")]
    [Authorize]
    public async Task<IActionResult> EnterLeague([FromBody] EnterLeagueRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var code = req.LeagueCode.Trim().ToUpperInvariant();

        var player = await db.Players
            .Include(p => p.League)
            .FirstOrDefaultAsync(p => p.LeagueCode == code && p.UserId == userId);

        if (player is null) return NotFound("You are not a member of this league.");

        // Generate a fresh session token. Hash it for storage; return the plaintext to the client.
        // The client stores this as their "PIN" for all subsequent authenticated API calls.
        var newToken = Guid.NewGuid().ToString();
        var tokenHash = BC.HashPassword(newToken);
        player.Pin = tokenHash;

        // Keep league.AdminPin in sync for the commissioner so VerifyAdminPin still works.
        if (player.League.CommissionerPlayerId == player.Id)
            player.League.AdminPin = tokenHash;

        await db.SaveChangesAsync();

        bool isCommissioner = player.League.CommissionerPlayerId == player.Id;
        bool isAdmin = isCommissioner || player.IsCoCommissioner;
        return Ok(new JoinResponse(
            player.Id, player.Name, isAdmin, isCommissioner,
            player.LeagueCode, player.TeamName, player.TeamImageUrl,
            player.TimeZone, player.Availability, player.League.Name,
            newToken
        ));
    }

    /// <summary>Redirects to Discord OAuth2 authorization. Stores a state cookie for CSRF protection.</summary>
    [HttpGet("discord")]
    public IActionResult DiscordLogin()
    {
        var clientId = config["Discord:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(503, "Discord authentication is not configured.");

        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var isLocalhost = HttpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        Response.Cookies.Append("discord_oauth_state", state, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isLocalhost,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10),
        });

        var redirectUri = Uri.EscapeDataString(config["Discord:RedirectUri"] ?? "");
        var url = $"https://discord.com/oauth2/authorize?client_id={clientId}&redirect_uri={redirectUri}&response_type=code&scope=identify%20email&state={Uri.EscapeDataString(state)}";
        return Redirect(url);
    }

    /// <summary>Discord OAuth2 callback. Exchanges code for user info, upserts AppUser, issues JWT,
    /// and redirects to the frontend callback page with the token in the URL fragment.</summary>
    [HttpGet("discord/callback")]
    public async Task<IActionResult> DiscordCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        var frontendUrl = config["Discord:FrontendUrl"] ?? string.Empty;
        const string callbackPath = "/auth/discord/callback";

        // Validate CSRF state
        var cookieState = Request.Cookies["discord_oauth_state"];
        Response.Cookies.Delete("discord_oauth_state");
        if (string.IsNullOrWhiteSpace(cookieState) || cookieState != state)
            return Redirect($"{frontendUrl}{callbackPath}#error=state_mismatch");

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
            return Redirect($"{frontendUrl}{callbackPath}#error=access_denied");

        try
        {
            var http = httpClientFactory.CreateClient("discord");

            // Exchange authorization code for access token
            using var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/oauth2/token");
            tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config["Discord:ClientId"]!,
                ["client_secret"] = config["Discord:ClientSecret"]!,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = config["Discord:RedirectUri"]!,
            });
            var tokenResp = await http.SendAsync(tokenReq);
            if (!tokenResp.IsSuccessStatusCode)
            {
                var tokenError = await tokenResp.Content.ReadAsStringAsync();
                logger.LogWarning(
                    "Discord token exchange failed with status {StatusCode}: {Error}",
                    tokenResp.StatusCode,
                    tokenError);
                return Redirect($"{frontendUrl}{callbackPath}#error=token_exchange_failed");
            }

            using var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync());
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenEl))
            {
                logger.LogWarning("Discord token exchange response did not include an access_token.");
                return Redirect($"{frontendUrl}{callbackPath}#error=no_access_token");
            }
            var accessToken = accessTokenEl.GetString()!;

            // Fetch Discord user profile
            using var userReq = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
            userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var userResp = await http.SendAsync(userReq);
            if (!userResp.IsSuccessStatusCode)
            {
                var userError = await userResp.Content.ReadAsStringAsync();
                logger.LogWarning(
                    "Discord user fetch failed with status {StatusCode}: {Error}",
                    userResp.StatusCode,
                    userError);
                return Redirect($"{frontendUrl}{callbackPath}#error=user_fetch_failed");
            }

            using var userDoc = await JsonDocument.ParseAsync(await userResp.Content.ReadAsStreamAsync());
            var root = userDoc.RootElement;
            if (!root.TryGetProperty("id", out var idEl) || !root.TryGetProperty("username", out var usernameEl))
            {
                logger.LogWarning("Discord user response did not include the expected id and username fields.");
                return Redirect($"{frontendUrl}{callbackPath}#error=invalid_user_data");
            }

            var discordId = idEl.GetString()!;
            var username = usernameEl.GetString()!;
            var email = root.TryGetProperty("email", out var emailEl) ? emailEl.GetString() ?? string.Empty : string.Empty;
            var avatarHash = root.TryGetProperty("avatar", out var avatarEl) ? avatarEl.GetString() : null;
            var pictureUrl = avatarHash != null
                ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatarHash}.png"
                : string.Empty;

            // Upsert AppUser
            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.DiscordId == discordId);
            if (user is null)
            {
                user = new AppUser { DiscordId = discordId, Email = email, Name = username, PictureUrl = pictureUrl };
                db.AppUsers.Add(user);
            }
            else
            {
                user.Name = username;
                user.PictureUrl = pictureUrl;
                if (!string.IsNullOrEmpty(email)) user.Email = email;
            }
            await db.SaveChangesAsync();

            var jwt = IssueJwt(user);
            return Redirect($"{frontendUrl}{callbackPath}#token={Uri.EscapeDataString(jwt)}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discord OAuth callback failed.");
            return Redirect($"{frontendUrl}{callbackPath}#error=server_error");
        }
    }
}
