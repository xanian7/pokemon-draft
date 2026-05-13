using PokemonDraft.DTOs;

namespace PokemonDraft.Services;

public interface IPokemonService
{
    Task<List<PokemonResponse>> GetAllPokemon();
    Task<PokemonResponse?> GetPokemonById(int id);
}