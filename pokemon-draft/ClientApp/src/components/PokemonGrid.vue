<script setup lang="ts">
import { usePokemonStore } from '@/stores/pokemon';
import PokemonCard from './PokemonCard.vue';
import { computed, reactive, ref } from 'vue';
import SearchBox from './SearchBox.vue';
import PokemonDetailModal from './PokemonDetailModal.vue';
import type { Pokemon } from '@/types';
import { useDraftStore } from '@/stores/draft';
import { useAuthStore } from '@/stores/auth';

const pokemonStore = usePokemonStore();
const draftStore = useDraftStore();
const authStore = useAuthStore()
const searchQuery = ref('')

const pageData = reactive({
    showPokemonDetail: false,
    selectedPokemon: null as Pokemon | null,
})

const filteredPokemon = computed(() => {
    const query = searchQuery.value.toLowerCase()
    return pokemonStore.pokemonWithPoints.filter((p) =>
        p.name.toLowerCase().includes(query)
    )
})

const canDraft = computed(() => {
    return !draftStore.isDraftComplete && draftStore.playerCanDraft(authStore.playerId ?? '')
})

function selectPokemon(pokemon: Pokemon) {
    pageData.selectedPokemon = pokemon
    pageData.showPokemonDetail = true
}

async function handleDraft(pokemonId: number) {
    await draftStore.makePick(pokemonId)
    pageData.showPokemonDetail = false
}
</script>

<template>
    <div class="wrapper">
        <div class="pokemon-grid-header">
            <search-box v-model="searchQuery" />
        </div>
        <div class="pokemon-grid">
            <pokemon-card
            v-for="pokemon in filteredPokemon"
            :key="pokemon.id"
            :pokemon="pokemon"
            :isPicked="draftStore.pickedPokemonIds.has(pokemon.id)"
            :pointValue="pokemonStore.getPointValue(pokemon.id)"
            @click="selectPokemon(pokemon)"
            />
        </div>
        <pokemon-detail-modal
            v-if="pageData.showPokemonDetail && pageData.selectedPokemon"
            :pokemon="pageData.selectedPokemon"
            :canDraft="canDraft"
            :isPicked="draftStore.pickedPokemonIds.has(pageData.selectedPokemon.id)"
            :pointValue="pokemonStore.getPointValue(pageData.selectedPokemon.id)"
            @close="pageData.showPokemonDetail = false"
            @draft="handleDraft"
        />
    </div>
</template>

<style scoped>
.wrapper {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0; /* required for flex children to shrink below content size */
  overflow: hidden;
}

.pokemon-grid-header {
  flex-shrink: 0;
  padding-bottom: 8px;
}
</style>