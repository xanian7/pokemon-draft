using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using PokemonDraft.DTOs;

namespace PokemonDraft.Services;

public class PokemonService(HttpClient httpClient, IMemoryCache cache) : IPokemonService
{
    private const string CacheKey = "all-pokemon";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private const string GraphQlEndpoint = "https://graphql.pokeapi.co/v1beta2";

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
        if (cache.TryGetValue(CacheKey, out List<PokemonResponse>? cached) && cached is not null)
            return cached;

        var payload = JsonSerializer.Serialize(new { query = GraphQlQuery });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(GraphQlEndpoint, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<PokeApiResponse>(json)
            ?? throw new InvalidOperationException("Failed to deserialize PokeAPI response.");

        var pokemon = apiResponse.Data.Pokemon
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

        cache.Set(CacheKey, pokemon, CacheDuration);
        return pokemon;
    }

    public async Task<PokemonResponse?> GetPokemonById(int id)
    {
        var all = await GetAllPokemon();
        return all.Find(p => p.Id == id);
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
        if (cache.TryGetValue(cacheKey, out PokemonDetailResponse? cached) && cached is not null)
            return cached;

        var payload = JsonSerializer.Serialize(new { query = GraphQlDetailQuery, variables = new { id } });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(GraphQlEndpoint, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<DetailApiResponse>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Pokémon detail response.");

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

        cache.Set(cacheKey, detail, CacheDuration);
        return detail;
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
}
