using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
BracketMatch Match(string id, string a, string b, int? winner = null) =>
    new(id, [new(a, null), new(b, null)], winner);
BracketRound Round(string id, params BracketMatch[] matches) => new(id, id, matches.ToList());
BracketConfiguration Config(params BracketRound[] rounds) => new([], rounds.ToList());
HashSet<string> players = ["a", "b", "c"];
string? Validate(BracketConfiguration config) => BracketValidator.Validate(config, players);

Check(Validate(Config(Round("final", Match("m", "", "")))) is null, "Incomplete draft brackets can be saved");
Check(Validate(Config(Round("final", Match("m", "player:a", "player:a")))) is not null, "Duplicate players rejected");
Check(Validate(Config(Round("final", Match("m", "player:outsider", "bye")))) is not null, "Foreign league players rejected");
Check(Validate(Config(Round("final", Match("m", "winner:m", "bye")))) is not null, "Cycles rejected");
Check(Validate(Config(Round("first", Match("a", "winner:b", "bye")), Round("second", Match("b", "", "")))) is not null, "Forward references rejected");
Check(Validate(Config(Round("final", Match("m", "player:a", "", 0)))) is not null, "Unresolved opponent cannot be defeated");
Check(Validate(Config(Round("first", Match("m", "player:a", "bye")), Round("final", Match("n", "winner:m", "player:b", 0)))) is null, "Byes advance into later rounds");
var playIn = new BracketConfiguration(
    [Round("play-in", Match("m", "player:a", "player:b", 0))],
    [Round("final", Match("n", "winner:m", "loser:m", 1))]);
Check(Validate(playIn) is null, "Play-in winner and loser feed playoff slots");
Check(Validate(Config(Round("final", new BracketMatch("m", [new("player:a", 1), new("player:b", 1)], null)))) is not null, "Duplicate seeds rejected");
Check(Validate(Config(Round("final", Match("m", "player:a", "player:b", 2)))) is not null, "Invalid winner side rejected");
Check(Validate(Config(Round("final", Match("m", "bye", "bye", 0)))) is not null, "A bye cannot be selected as winner");
Check(Validate(new BracketConfiguration([], [])) is not null, "Empty postseason rejected");

using var db = new DraftDbContext(new DbContextOptionsBuilder<DraftDbContext>()
    .UseSqlServer("Server=localhost;Database=BracketChecks;Trusted_Connection=True;TrustServerCertificate=True").Options);
Check(!db.Database.HasPendingModelChanges(), "Migration snapshot matches the runtime model");
var sql = db.GetService<IMigrator>().GenerateScript("20260831230146_AddReplayAbility", "20260916010000_AddPlayoffBracket");
Check(sql.Contains("CREATE TABLE [PlayoffBrackets]") && sql.Contains("ON DELETE CASCADE"), "Migration creates persisted league brackets with cascade cleanup");
Console.WriteLine(checks + " bracket checks passed.");
