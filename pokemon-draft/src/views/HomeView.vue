<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useLeagueStore } from '@/stores/league'
import { useDraftStore } from '@/stores/draft'
import { usePokemonStore } from '@/stores/pokemon'
import AppIcon from '@/components/AppIcon.vue'
import {
  mdiTrophy,
  mdiCog,
  mdiClipboardList,
  mdiCircle,
  mdiCheckCircle,
  mdiCircleOutline,
  mdiCards,
} from '@mdi/js'

const router = useRouter()
const leagueStore = useLeagueStore()
const draftStore = useDraftStore()
const pokemonStore = usePokemonStore()

const valuedPokemonCount = computed(() => Object.keys(pokemonStore.pointValues).length)

const statusInfo = computed(() => {
  if (draftStore.status === 'active')
    return { text: 'In Progress', icon: mdiCircle, color: '#10b981' }
  if (draftStore.status === 'complete')
    return { text: 'Complete', icon: mdiCheckCircle, color: '#10b981' }
  return { text: 'Not Started', icon: mdiCircleOutline, color: 'var(--text-muted)' }
})
</script>

<template>
  <main class="home">
    <section class="hero">
      <h1>
        <AppIcon :path="mdiCards" :size="32" />
        Pokémon Draft League
      </h1>
      <p>Build your league, assign point values, and run a snake draft.</p>
    </section>

    <section class="status-cards">
      <div class="stat-card">
        <span class="stat-label">League</span>
        <span class="stat-value">{{ leagueStore.config.name || '—' }}</span>
      </div>
      <div class="stat-card">
        <span class="stat-label">Players</span>
        <span class="stat-value">{{ leagueStore.config.players.length }}</span>
      </div>
      <div class="stat-card">
        <span class="stat-label">Point Limit</span>
        <span class="stat-value">{{ leagueStore.config.pointLimit }} pts</span>
      </div>
      <div class="stat-card">
        <span class="stat-label">Rounds</span>
        <span class="stat-value">{{ leagueStore.config.rounds }}</span>
      </div>
      <div class="stat-card">
        <span class="stat-label">Valued Pokémon</span>
        <span class="stat-value">{{ valuedPokemonCount }}</span>
      </div>
      <div class="stat-card">
        <span class="stat-label">Draft Status</span>
        <span class="stat-value status-value">
          <AppIcon :path="statusInfo.icon" :size="16" :style="{ color: statusInfo.color }" />
          {{ statusInfo.text }}
        </span>
      </div>
    </section>

    <section class="actions">
      <button class="action-btn primary" @click="router.push('/league/setup')">
        <AppIcon :path="mdiCog" :size="18" />
        Configure League
      </button>
      <button class="action-btn secondary" @click="router.push('/pokemon')">
        <AppIcon :path="mdiClipboardList" :size="18" />
        Manage Point Values
      </button>
      <button class="action-btn accent" @click="router.push('/draft')">
        <AppIcon :path="mdiTrophy" :size="18" />
        Go to Draft Board
      </button>
    </section>

    <section class="how-it-works">
      <h2>How It Works</h2>
      <ol>
        <li>
          <strong>Configure your league</strong> — set the league name, add players, define the
          point limit per team, and number of draft rounds.
        </li>
        <li>
          <strong>Assign point values</strong> — browse all Pokémon and set a point cost for each
          one. Higher-tier Pokémon cost more points.
        </li>
        <li>
          <strong>Run the snake draft</strong> — players take turns picking Pokémon. The order
          reverses each round (snake format), and point costs are tracked per team.
        </li>
      </ol>
    </section>
  </main>
</template>

<style scoped>
.home {
  max-width: 860px;
  margin: 0 auto;
  padding: 2rem 1rem;
}

.hero {
  text-align: center;
  margin-bottom: 2rem;
}

.hero h1 {
  font-size: 2rem;
  font-weight: 800;
  margin-bottom: 0.5rem;
  color: var(--text);
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
}

.hero p {
  color: var(--text-muted);
  font-size: 1.05rem;
}

.status-cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 1rem;
  margin-bottom: 2rem;
}

.stat-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.stat-label {
  font-size: 0.75rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.stat-value {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--text);
}

.status-value {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.95rem;
}

.actions {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 2.5rem;
}

.action-btn {
  flex: 1;
  min-width: 160px;
  padding: 0.85rem 1.25rem;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition:
    opacity 0.15s,
    transform 0.1s;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
}

.action-btn:hover {
  opacity: 0.88;
  transform: translateY(-1px);
}

.action-btn.primary {
  background: var(--primary);
  color: white;
}

.action-btn.secondary {
  background: var(--secondary);
  color: white;
}

.action-btn.accent {
  background: #2a9d8f;
  color: white;
}

.how-it-works {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 1.5rem;
}

.how-it-works h2 {
  margin-top: 0;
  margin-bottom: 1rem;
  font-size: 1.1rem;
  color: var(--text);
}

.how-it-works ol {
  margin: 0;
  padding-left: 1.4rem;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  color: var(--text-muted);
  line-height: 1.6;
}

.how-it-works strong {
  color: var(--text);
}
</style>
