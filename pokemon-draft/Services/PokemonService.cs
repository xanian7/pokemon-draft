using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using PokemonDraft.DTOs;

namespace PokemonDraft.Services;

public class PokemonService(HttpClient httpClient, IMemoryCache cache) : IPokemonService
{
    private const string CacheKey = "all-pokemon";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private const string GraphQlEndpoint = "https://beta.pokeapi.co/graphql/v1beta";

    private static readonly string[] CosmeticFormPrefixes =
        ["pikachu-", "minior-", "squawkabilly-", "koraidon-", "miraidon-"];

    private static readonly HashSet<string> CosmeticFormNames =
        ["eevee-starter", "magearna-original", "zarude-dada", "gimmighoul-roaming", "keldeo-resolute"];

    private const string GraphQlQuery = """
        query {
          pokemon_v2_pokemon(
            where: {
              pokemon_v2_pokemonforms: {
                is_battle_only: { _eq: false }
                is_mega: { _eq: false }
              }
            }
            order_by: { id: asc }
          ) {
            id
            name
            is_default
            pokemon_v2_pokemonspecy {
              id
            }
            pokemon_v2_pokemontypes {
              pokemon_v2_type {
                name
              }
            }
            pokemon_v2_pokemonstats {
              base_stat
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
                SpriteUrl: $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{p.Id}.png",
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
          pokemon_v2_pokemon_by_pk(id: $id) {
            pokemon_v2_pokemonstats {
              base_stat
              pokemon_v2_stat { name }
            }
            pokemon_v2_pokemonabilities {
              is_hidden
              pokemon_v2_ability { name }
            }
            pokemon_v2_pokemonmoves(distinct_on: move_id) {
              pokemon_v2_move {
                name
                power
                pp
                pokemon_v2_type { name }
                pokemon_v2_movedamageclass { name }
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

        var raw = apiResponse.Data.Pokemon;
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

    // ---- Internal deserialization models ----

    private sealed class PokeApiResponse
    {
        [JsonPropertyName("data")]
        public PokeApiData Data { get; set; } = new();
    }

    private sealed class PokeApiData
    {
        [JsonPropertyName("pokemon_v2_pokemon")]
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

        [JsonPropertyName("pokemon_v2_pokemonspecy")]
        public PokeApiSpecies? Species { get; set; }

        [JsonPropertyName("pokemon_v2_pokemontypes")]
        public List<PokeApiTypeEntry> Types { get; set; } = [];

        [JsonPropertyName("pokemon_v2_pokemonstats")]
        public List<PokeApiStatEntry> Stats { get; set; } = [];
    }

    private sealed class PokeApiSpecies
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    private sealed class PokeApiTypeEntry
    {
        [JsonPropertyName("pokemon_v2_type")]
        public PokeApiType Type { get; set; } = new();
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
        [JsonPropertyName("pokemon_v2_pokemon_by_pk")]
        public DetailApiPokemon? Pokemon { get; set; }
    }

    private sealed class DetailApiPokemon
    {
        [JsonPropertyName("pokemon_v2_pokemonstats")]
        public List<DetailApiStat> Stats { get; set; } = [];

        [JsonPropertyName("pokemon_v2_pokemonabilities")]
        public List<DetailApiAbility> Abilities { get; set; } = [];

        [JsonPropertyName("pokemon_v2_pokemonmoves")]
        public List<DetailApiMoveEntry> Moves { get; set; } = [];
    }

    private sealed class DetailApiStat
    {
        [JsonPropertyName("base_stat")]
        public int BaseStat { get; set; }

        [JsonPropertyName("pokemon_v2_stat")]
        public DetailApiNamedNode Stat { get; set; } = new();
    }

    private sealed class DetailApiAbility
    {
        [JsonPropertyName("is_hidden")]
        public bool IsHidden { get; set; }

        [JsonPropertyName("pokemon_v2_ability")]
        public DetailApiNamedNode Ability { get; set; } = new();
    }

    private sealed class DetailApiMoveEntry
    {
        [JsonPropertyName("pokemon_v2_move")]
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

        [JsonPropertyName("pokemon_v2_type")]
        public DetailApiNamedNode? Type { get; set; }

        [JsonPropertyName("pokemon_v2_movedamageclass")]
        public DetailApiNamedNode? DamageClass { get; set; }
    }

    private sealed class DetailApiNamedNode
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
