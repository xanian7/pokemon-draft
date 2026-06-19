using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Models;

namespace PokemonDraft.Services;

public class PokemonService(HttpClient httpClient, IMemoryCache cache, DraftDbContext db) : IPokemonService
{
    private const string CacheKey = "all-pokemon";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private const string GraphQlEndpoint = "https://graphql.pokeapi.co/v1beta2";
    private const string RestEndpoint = "https://pokeapi.co/api/v2";
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    private static readonly string[] CosmeticFormPrefixes =
        ["pikachu-", "minior-", "squawkabilly-", "koraidon-", "miraidon-"];

    private static readonly HashSet<string> CosmeticFormNames =
        ["eevee-starter", "magearna-original", "zarude-dada", "gimmighoul-roaming", "keldeo-resolute"];

    private const string GraphQlQuery = """
        query {
          pokemon(
            where: {
              pokemonforms: {
                is_battle_only: { _eq: false }
                is_mega: { _eq: false }
              }
            }
            order_by: { id: asc }
          ) {
            id
            name
            is_default
            pokemonspecy {
              id
            }
            pokemontypes {
              type {
                name
              }
            }
            pokemonstats {
              base_stat
            }
            pokemonsprites {
              modern: sprites(path: "other.home.front_default")
              artwork: sprites(path: "other.official-artwork.front_default")
              default: sprites(path: "front_default")
            }
          }
        }
        """;

    public async Task<List<PokemonResponse>> GetAllPokemon()
    {
        if (cache.TryGetValue(CacheKey, out List<PokemonResponse>? cached) && cached is { Count: > 0 })
            return cached;

        cache.Remove(CacheKey);

        await EnsurePokemonCachePopulated();
        var pokemon = await LoadAllPokemonFromDatabase();

        cache.Set(CacheKey, pokemon, CacheDuration);
        return pokemon;
    }

    private async Task<List<PokemonResponse>> LoadAllPokemonFromDatabase()
    {
        var rows = await db.PokemonCache
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync();

        return rows
            .Select(p => new PokemonResponse(
                p.Id,
                p.SpeciesId,
                p.Name,
                p.SpriteUrl,
                JsonSerializer.Deserialize<List<string>>(p.TypesJson) ?? new List<string>(),
                p.Bst
            ))
            .ToList();
    }

    private async Task EnsurePokemonCachePopulated()
    {
        if (await PokemonCacheHasRows())
            return;

        await ImportLock.WaitAsync();
        try
        {
            if (await PokemonCacheHasRows())
                return;

            var pokemon = await GetAllPokemonFromGraphQl();
            if (pokemon.Count == 0)
                pokemon = await GetAllPokemonFromRest();

            if (pokemon.Count == 0)
                throw new InvalidOperationException("PokeAPI returned an empty Pokemon list.");

            using var throttle = new SemaphoreSlim(8);
            var importTasks = pokemon.Select(async summary =>
            {
                await throttle.WaitAsync();
                try
                {
                    var detail = await FetchPokemonDetailFromApi(summary.Id);
                    var megas = await GetMegaFormsFromRest(summary.SpeciesId);
                    return new PokemonCache
                    {
                        Id = summary.Id,
                        SpeciesId = summary.SpeciesId,
                        Name = summary.Name,
                        SpriteUrl = summary.SpriteUrl,
                        TypesJson = JsonSerializer.Serialize(summary.Types),
                        Bst = summary.Bst,
                        DetailJson = detail is null
                            ? string.Empty
                            : JsonSerializer.Serialize(detail with { MegaForms = megas }),
                        MegaFormsJson = JsonSerializer.Serialize(megas),
                        ImportedAt = DateTime.UtcNow,
                    };
                }
                finally
                {
                    throttle.Release();
                }
            });

            db.PokemonCache.AddRange(await Task.WhenAll(importTasks));

            await db.SaveChangesAsync();
        }
        finally
        {
            ImportLock.Release();
        }
    }

    private async Task<bool> PokemonCacheHasRows()
    {
        return await db.PokemonCache.AnyAsync();
    }

    private async Task<List<PokemonResponse>> GetAllPokemonFromGraphQl()
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { query = GraphQlQuery });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(GraphQlEndpoint, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<PokeApiResponse>(json)
                ?? throw new InvalidOperationException("Failed to deserialize PokeAPI response.");

            return apiResponse.Data.Pokemon
                .Where(p => !IsCosmeticForm(p.Name, p.IsDefault))
                .Select(p => new PokemonResponse(
                    Id: p.Id,
                    SpeciesId: p.Species?.Id ?? p.Id,
                    Name: p.Name,
                    SpriteUrl: GetSpriteUrl(p),
                    Types: p.Types.Select(t => t.Type.Name).ToList(),
                    Bst: p.Stats.Sum(s => s.BaseStat)
                ))
                .ToList();
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private async Task<List<PokemonResponse>> GetAllPokemonFromRest()
    {
        var response = await httpClient.GetAsync($"{RestEndpoint}/pokemon?limit=2000");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var list = JsonSerializer.Deserialize<RestPokemonList>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Pokemon REST list response.");

        using var throttle = new SemaphoreSlim(16);
        var tasks = list.Results.Select(async item =>
        {
            await throttle.WaitAsync();
            try
            {
                return await GetPokemonSummaryFromRest(item.Url);
            }
            catch (HttpRequestException)
            {
                return null;
            }
            finally
            {
                throttle.Release();
            }
        });

        var pokemon = await Task.WhenAll(tasks);
        return pokemon
            .Where(p => p is not null)
            .Cast<PokemonResponse>()
            .OrderBy(p => p.Id)
            .ToList();
    }

    private async Task<PokemonResponse?> GetPokemonSummaryFromRest(string url)
    {
        var response = await httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var pokemon = JsonSerializer.Deserialize<RestPokemonDetail>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Pokemon REST summary response.");

        if (IsCosmeticForm(pokemon.Name, pokemon.IsDefault))
            return null;

        return new PokemonResponse(
            Id: pokemon.Id,
            SpeciesId: GetIdFromResourceUrl(pokemon.Species.Url) ?? pokemon.Id,
            Name: pokemon.Name,
            SpriteUrl: GetSpriteUrl(pokemon),
            Types: pokemon.Types.Select(t => t.Type.Name).ToList(),
            Bst: pokemon.Stats.Sum(s => s.BaseStat)
        );
    }

    private static PokemonResponse ToPokemonResponse(PokemonCache pokemon)
    {
        return new PokemonResponse(
            pokemon.Id,
            pokemon.SpeciesId,
            pokemon.Name,
            pokemon.SpriteUrl,
            JsonSerializer.Deserialize<List<string>>(pokemon.TypesJson) ?? [],
            pokemon.Bst
        );
    }

    public async Task<PokemonResponse?> GetPokemonById(int id)
    {
        await EnsurePokemonCachePopulated();
        var pokemon = await db.PokemonCache.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return pokemon is null ? null : ToPokemonResponse(pokemon);
    }

    private const string DetailCacheKeyPrefix = "pokemon-detail-";

    private const string GraphQlDetailQuery = """
        query PokemonDetail($id: Int!) {
          pokemon(where: { id: { _eq: $id } }, limit: 1) {
            pokemonstats {
              base_stat
              stat { name }
            }
            pokemonabilities {
              is_hidden
              ability { name }
            }
            pokemonmoves(distinct_on: move_id) {
              move {
                name
                power
                pp
                type { name }
                movedamageclass { name }
              }
            }
          }
        }
        """;

    public async Task<PokemonDetailResponse?> GetPokemonDetail(int id)
    {
        var cacheKey = DetailCacheKeyPrefix + id;
        if (cache.TryGetValue(cacheKey, out PokemonDetailResponse? cached) && cached is not null && HasUsableDetail(cached))
            return cached;

        cache.Remove(cacheKey);

        await EnsurePokemonCachePopulated();
        var cachedPokemon = await db.PokemonCache.FirstOrDefaultAsync(p => p.Id == id);
        if (cachedPokemon is null)
            return null;

        if (!string.IsNullOrWhiteSpace(cachedPokemon.DetailJson))
        {
            var cachedDetail = JsonSerializer.Deserialize<PokemonDetailResponse>(cachedPokemon.DetailJson);
            if (cachedDetail is not null && HasUsableDetail(cachedDetail))
            {
                var mergedDetail = cachedDetail with
                {
                    MegaForms = GetCachedMegaForms(cachedDetail, cachedPokemon),
                };

                if (!ReferenceEquals(mergedDetail, cachedDetail))
                {
                    cachedPokemon.DetailJson = JsonSerializer.Serialize(mergedDetail);
                    await db.SaveChangesAsync();
                }

                cache.Set(cacheKey, mergedDetail, CacheDuration);
                return mergedDetail;
            }
        }

        var detail = await FetchPokemonDetailFromApi(id);
        if (detail is null)
            return null;

        var megas = await GetMegaFormsFromRest(cachedPokemon.SpeciesId);
        detail = detail with { MegaForms = megas };
        cachedPokemon.DetailJson = JsonSerializer.Serialize(detail);
        cachedPokemon.MegaFormsJson = JsonSerializer.Serialize(megas);
        cachedPokemon.ImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        cache.Set(cacheKey, detail, CacheDuration);
        return detail;
    }

    private async Task<PokemonDetailResponse?> FetchPokemonDetailFromApi(int id)
    {
        var payload = JsonSerializer.Serialize(new { query = GraphQlDetailQuery, variables = new { id } });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var apiResponse = await GetPokemonDetailFromGraphQl(content);
        if (apiResponse is null)
            return await GetPokemonDetailFromRest(id);

        var raw = apiResponse.Data.Pokemon.FirstOrDefault();
        if (raw is null) return null;

        var detail = new PokemonDetailResponse(
            Stats: raw.Stats.Select(s => new PokemonDetailStat(s.Stat.Name, s.BaseStat)).ToList(),
            Abilities: raw.Abilities.Select(a => new PokemonDetailAbility(a.Ability.Name, a.IsHidden)).ToList(),
            Moves: raw.Moves
                .Select(m => new PokemonDetailMove(
                    Name: m.Move.Name,
                    Type: m.Move.Type?.Name ?? "normal",
                    Power: m.Move.Power,
                    Pp: m.Move.Pp,
                    Category: m.Move.DamageClass?.Name ?? "status"
                ))
                .OrderBy(m => m.Name)
                .ToList()
        );

        return HasUsableDetail(detail) ? detail : await GetPokemonDetailFromRest(id);
    }

    private static bool HasUsableDetail(PokemonDetailResponse detail)
    {
        return (detail.Stats.Count > 0 || detail.Abilities.Count > 0)
            && HasUsableMoveDetails(detail.Moves);
    }

    private static bool HasUsableMoveDetails(List<PokemonDetailMove> moves)
    {
        if (moves.Count == 0)
            return false;

        return moves.Any(move =>
            move.Type != "normal" ||
            move.Category != "status" ||
            move.Power is not null ||
            move.Pp is not null);
    }

    private static List<PokemonMegaFormResponse> GetCachedMegaForms(
        PokemonDetailResponse detail,
        PokemonCache pokemon)
    {
        if (detail.MegaForms is { Count: > 0 })
            return detail.MegaForms;

        if (string.IsNullOrWhiteSpace(pokemon.MegaFormsJson))
            return [];

        return JsonSerializer.Deserialize<List<PokemonMegaFormResponse>>(pokemon.MegaFormsJson) ?? [];
    }

    private async Task<DetailApiResponse?> GetPokemonDetailFromGraphQl(StringContent content)
    {
        try
        {
            var response = await httpClient.PostAsync(GraphQlEndpoint, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<DetailApiResponse>(json)
                ?? throw new InvalidOperationException("Failed to deserialize Pokemon detail response.");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<PokemonDetailResponse?> GetPokemonDetailFromRest(int id)
    {
        var response = await httpClient.GetAsync($"{RestEndpoint}/pokemon/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var pokemon = JsonSerializer.Deserialize<RestPokemonDetail>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Pokemon REST detail response.");
        var moves = await GetMovesFromRest(pokemon.Moves);

        return new PokemonDetailResponse(
            Stats: pokemon.Stats
                .Select(s => new PokemonDetailStat(s.Stat.Name, s.BaseStat))
                .ToList(),
            Abilities: pokemon.Abilities
                .Select(a => new PokemonDetailAbility(a.Ability.Name, a.IsHidden))
                .ToList(),
            Moves: moves
        );
    }

    private async Task<List<PokemonDetailMove>> GetMovesFromRest(List<RestMoveEntry> moveEntries)
    {
        using var throttle = new SemaphoreSlim(16);
        var tasks = moveEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Move.Url))
            .Select(async entry =>
            {
                await throttle.WaitAsync();
                try
                {
                    return await GetMoveFromRest(entry.Move.Url);
                }
                catch (HttpRequestException)
                {
                    return null;
                }
                finally
                {
                    throttle.Release();
                }
            });

        var moves = await Task.WhenAll(tasks);
        return moves
            .Where(move => move is not null)
            .Cast<PokemonDetailMove>()
            .OrderBy(move => move.Name)
            .ToList();
    }

    private async Task<PokemonDetailMove?> GetMoveFromRest(string url)
    {
        var response = await httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var move = JsonSerializer.Deserialize<RestMoveDetail>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Pokemon REST move response.");

        return new PokemonDetailMove(
            Name: move.Name,
            Type: move.Type?.Name ?? "normal",
            Power: move.Power,
            Pp: move.Pp,
            Category: move.DamageClass?.Name ?? "status"
        );
    }

    private static bool IsCosmeticForm(string name, bool isDefault)
    {
        if (isDefault) return false;
        if (name.EndsWith("-gmax")) return true;
        if (name.EndsWith("-totem") || name.Contains("-totem-")) return true;
        if (name.Contains("-power-construct")) return true;
        if (CosmeticFormNames.Contains(name)) return true;
        return CosmeticFormPrefixes.Any(name.StartsWith);
    }

    private static string GetSpriteUrl(PokeApiPokemon pokemon)
    {
        var sprites = pokemon.Sprites.FirstOrDefault();
        return sprites?.Modern
            ?? sprites?.Artwork
            ?? sprites?.Default
            ?? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{pokemon.Id}.png";
    }

    private static string GetSpriteUrl(RestPokemonDetail pokemon)
    {
        return pokemon.Sprites.Other?.Home?.FrontDefault
            ?? pokemon.Sprites.Other?.OfficialArtwork?.FrontDefault
            ?? pokemon.Sprites.FrontDefault
            ?? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{pokemon.Id}.png";
    }

    private static int? GetIdFromResourceUrl(string url)
    {
        var trimmed = url.TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');
        return slashIndex >= 0 && int.TryParse(trimmed[(slashIndex + 1)..], out var id) ? id : null;
    }

    private async Task<List<PokemonMegaFormResponse>> GetMegaFormsFromRest(int speciesId)
    {
        try
        {
            var response = await httpClient.GetAsync($"{RestEndpoint}/pokemon-species/{speciesId}");
            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync();
            var species = JsonSerializer.Deserialize<RestSpeciesDetail>(json);
            if (species is null)
                return [];

            var megaNames = species.Varieties
                .Select(v => v.Pokemon.Name)
                .Where(name => name.Contains("-mega"))
                .Distinct()
                .ToList();

            var forms = new List<PokemonMegaFormResponse>();
            foreach (var name in megaNames)
            {
                var form = await GetMegaFormFromRest(name);
                if (form is not null)
                    forms.Add(form);
            }

            return forms;
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private async Task<PokemonMegaFormResponse?> GetMegaFormFromRest(string name)
    {
        try
        {
            var response = await httpClient.GetAsync($"{RestEndpoint}/pokemon/{name}");
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var pokemon = JsonSerializer.Deserialize<RestPokemonDetail>(json);
            if (pokemon is null)
                return null;

            return new PokemonMegaFormResponse(
                Name: pokemon.Name,
                Label: ParseMegaLabel(pokemon.Name),
                Sprite: GetSpriteUrl(pokemon),
                Types: pokemon.Types.Select(t => t.Type.Name).ToList(),
                Stats: pokemon.Stats
                    .Select(s => new PokemonDetailStat(s.Stat.Name, s.BaseStat))
                    .ToList(),
                Abilities: pokemon.Abilities
                    .Select(a => new PokemonDetailAbility(a.Ability.Name, a.IsHidden))
                    .ToList()
            );
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static string ParseMegaLabel(string name)
    {
        var index = name.IndexOf("-mega", StringComparison.Ordinal);
        if (index < 0) return name;
        var label = name[(index + 1)..].Replace('-', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label);
    }

    // ---- Internal deserialization models ----

    private sealed class PokeApiResponse
    {
        [JsonPropertyName("data")]
        public PokeApiData Data { get; set; } = new();
    }

    private sealed class PokeApiData
    {
        [JsonPropertyName("pokemon")]
        public List<PokeApiPokemon> Pokemon { get; set; } = [];
    }

    private sealed class PokeApiPokemon
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("is_default")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("pokemonspecy")]
        public PokeApiSpecies? Species { get; set; }

        [JsonPropertyName("pokemontypes")]
        public List<PokeApiTypeEntry> Types { get; set; } = [];

        [JsonPropertyName("pokemonstats")]
        public List<PokeApiStatEntry> Stats { get; set; } = [];

        [JsonPropertyName("pokemonsprites")]
        public List<PokeApiSprites> Sprites { get; set; } = [];
    }

    private sealed class PokeApiSpecies
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    private sealed class PokeApiTypeEntry
    {
        [JsonPropertyName("type")]
        public PokeApiType Type { get; set; } = new();
    }

    private sealed class PokeApiSprites
    {
        [JsonPropertyName("modern")]
        public string? Modern { get; set; }

        [JsonPropertyName("artwork")]
        public string? Artwork { get; set; }

        [JsonPropertyName("default")]
        public string? Default { get; set; }
    }

    private sealed class PokeApiType
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PokeApiStatEntry
    {
        [JsonPropertyName("base_stat")]
        public int BaseStat { get; set; }
    }

    // ---- Detail deserialization models ----

    private sealed class DetailApiResponse
    {
        [JsonPropertyName("data")]
        public DetailApiData Data { get; set; } = new();
    }

    private sealed class DetailApiData
    {
        [JsonPropertyName("pokemon")]
        public List<DetailApiPokemon> Pokemon { get; set; } = [];
    }

    private sealed class DetailApiPokemon
    {
        [JsonPropertyName("pokemonstats")]
        public List<DetailApiStat> Stats { get; set; } = [];

        [JsonPropertyName("pokemonabilities")]
        public List<DetailApiAbility> Abilities { get; set; } = [];

        [JsonPropertyName("pokemonmoves")]
        public List<DetailApiMoveEntry> Moves { get; set; } = [];
    }

    private sealed class DetailApiStat
    {
        [JsonPropertyName("base_stat")]
        public int BaseStat { get; set; }

        [JsonPropertyName("stat")]
        public DetailApiNamedNode Stat { get; set; } = new();
    }

    private sealed class DetailApiAbility
    {
        [JsonPropertyName("is_hidden")]
        public bool IsHidden { get; set; }

        [JsonPropertyName("ability")]
        public DetailApiNamedNode Ability { get; set; } = new();
    }

    private sealed class DetailApiMoveEntry
    {
        [JsonPropertyName("move")]
        public DetailApiMove Move { get; set; } = new();
    }

    private sealed class DetailApiMove
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("power")]
        public int? Power { get; set; }

        [JsonPropertyName("pp")]
        public int? Pp { get; set; }

        [JsonPropertyName("type")]
        public DetailApiNamedNode? Type { get; set; }

        [JsonPropertyName("movedamageclass")]
        public DetailApiNamedNode? DamageClass { get; set; }
    }

    private sealed class DetailApiNamedNode
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    // ---- REST fallback deserialization models ----

    private sealed class RestPokemonDetail
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("is_default")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("species")]
        public RestNamedResource Species { get; set; } = new();

        [JsonPropertyName("types")]
        public List<RestTypeEntry> Types { get; set; } = [];

        [JsonPropertyName("stats")]
        public List<RestStatEntry> Stats { get; set; } = [];

        [JsonPropertyName("abilities")]
        public List<RestAbilityEntry> Abilities { get; set; } = [];

        [JsonPropertyName("moves")]
        public List<RestMoveEntry> Moves { get; set; } = [];

        [JsonPropertyName("sprites")]
        public RestSprites Sprites { get; set; } = new();
    }

    private sealed class RestPokemonList
    {
        [JsonPropertyName("results")]
        public List<RestNamedResource> Results { get; set; } = [];
    }

    private sealed class RestNamedResource
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    private sealed class RestTypeEntry
    {
        [JsonPropertyName("type")]
        public DetailApiNamedNode Type { get; set; } = new();
    }

    private sealed class RestStatEntry
    {
        [JsonPropertyName("base_stat")]
        public int BaseStat { get; set; }

        [JsonPropertyName("stat")]
        public DetailApiNamedNode Stat { get; set; } = new();
    }

    private sealed class RestAbilityEntry
    {
        [JsonPropertyName("is_hidden")]
        public bool IsHidden { get; set; }

        [JsonPropertyName("ability")]
        public DetailApiNamedNode Ability { get; set; } = new();
    }

    private sealed class RestMoveEntry
    {
        [JsonPropertyName("move")]
        public RestNamedResource Move { get; set; } = new();
    }

    private sealed class RestMoveDetail
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("power")]
        public int? Power { get; set; }

        [JsonPropertyName("pp")]
        public int? Pp { get; set; }

        [JsonPropertyName("type")]
        public DetailApiNamedNode? Type { get; set; }

        [JsonPropertyName("damage_class")]
        public DetailApiNamedNode? DamageClass { get; set; }
    }

    private sealed class RestSprites
    {
        [JsonPropertyName("front_default")]
        public string? FrontDefault { get; set; }

        [JsonPropertyName("other")]
        public RestSpriteOther? Other { get; set; }
    }

    private sealed class RestSpriteOther
    {
        [JsonPropertyName("home")]
        public RestSpriteSet? Home { get; set; }

        [JsonPropertyName("official-artwork")]
        public RestSpriteSet? OfficialArtwork { get; set; }
    }

    private sealed class RestSpriteSet
    {
        [JsonPropertyName("front_default")]
        public string? FrontDefault { get; set; }
    }

    private sealed class RestSpeciesDetail
    {
        [JsonPropertyName("varieties")]
        public List<RestVarietyEntry> Varieties { get; set; } = [];
    }

    private sealed class RestVarietyEntry
    {
        [JsonPropertyName("pokemon")]
        public RestNamedResource Pokemon { get; set; } = new();
    }
}
