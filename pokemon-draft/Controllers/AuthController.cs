using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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
public class AuthController(DraftDbContext db, IConfiguration config, ILeagueService leagueService) : ControllerBase
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
            p.League.CommissionerPlayerId == p.Id
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

        bool isAdmin = player.League.CommissionerPlayerId == player.Id;
        return Ok(new JoinResponse(
            player.Id, player.Name, isAdmin,
            player.LeagueCode, player.TeamName, player.TeamImageUrl, player.League.Name,
            newToken
        ));
    }
}
