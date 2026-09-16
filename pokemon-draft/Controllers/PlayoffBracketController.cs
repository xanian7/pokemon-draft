using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Models;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[ApiController]
[Route("api/leagues/{code}/playoff-bracket")]
public class PlayoffBracketController(DraftDbContext db, ILeagueService leagues) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string code)
    {
        code = code.ToUpperInvariant();
        if (!await db.Leagues.AnyAsync(l => l.Code == code)) return NotFound();
        var bracket = await db.PlayoffBrackets.AsNoTracking().SingleOrDefaultAsync(b => b.LeagueCode == code);
        return Ok(new BracketResponse(bracket?.Revision, bracket is null ? null :
            JsonSerializer.Deserialize<BracketConfiguration>(bracket.ConfigurationJson)));
    }

    [HttpPut]
    public async Task<IActionResult> Save(string code, SaveBracketRequest request)
    {
        code = code.ToUpperInvariant();
        var league = await db.Leagues.Include(l => l.Players).SingleOrDefaultAsync(l => l.Code == code);
        if (league is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.AdminPin) || leagues.ValidatePin(code, request.AdminPin)?.IsAdmin != true)
            return StatusCode(403, "Only commissioners can edit the postseason bracket.");
        var outlook = leagues.GetPlayoffOutlook(code);
        if (league.DraftStatus != DraftStatus.Complete || outlook is null || outlook.Count == 0 ||
            outlook.Any(p => p.RemainingMatchups > 0) || !await db.Matchups.AnyAsync(m => m.LeagueCode == code))
            return BadRequest("The regular season must be complete before saving a postseason bracket.");
        if (request.Configuration is null) return BadRequest("Bracket configuration is required.");
        var error = BracketValidator.Validate(request.Configuration, league.Players.Select(p => p.Id).ToHashSet());
        if (error is not null) return BadRequest(error);
        var bracket = await db.PlayoffBrackets.SingleOrDefaultAsync(b => b.LeagueCode == code);
        if (bracket?.Revision != request.Revision)
            return Conflict("The bracket was changed by another commissioner. Reload before saving.");
        if (bracket is null)
        {
            bracket = new PlayoffBracket { LeagueCode = code };
            db.PlayoffBrackets.Add(bracket);
        }
        bracket.ConfigurationJson = JsonSerializer.Serialize(request.Configuration);
        bracket.Revision = Guid.NewGuid();
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The bracket was changed by another commissioner. Reload before saving.");
        }
        catch (DbUpdateException) when (request.Revision is null)
        {
            if (await db.PlayoffBrackets.AsNoTracking().AnyAsync(b => b.LeagueCode == code))
                return Conflict("A bracket was just created by another commissioner. Reload before saving.");
            throw;
        }
        return Ok(new BracketResponse(bracket.Revision, request.Configuration));
    }
}
