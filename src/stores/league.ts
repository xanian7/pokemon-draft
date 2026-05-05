import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { LeagueConfig } from '@/types'

const STORAGE_KEY = 'pokemon-draft:league'

export const useLeagueStore = defineStore('league', () => {
  const config = ref<LeagueConfig>({
    name: 'My Draft League',
    players: [],
    pointLimit: 100,
    rounds: 6,
  })

  function load() {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      try {
        config.value = JSON.parse(stored)
      } catch {
        // ignore parse errors
      }
    }
  }

  function save() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(config.value))
  }

  function addPlayer(name: string) {
    const trimmed = name.trim()
    if (!trimmed) return
    config.value.players.push({ id: crypto.randomUUID(), name: trimmed })
    save()
  }

  function removePlayer(id: string) {
    config.value.players = config.value.players.filter((p) => p.id !== id)
    save()
  }

  function movePlayer(from: number, to: number) {
    const players = [...config.value.players]
    const moved = players[from]
    if (!moved) return
    players.splice(from, 1)
    players.splice(to, 0, moved)
    config.value.players = players
    save()
  }

  function updateConfig(updates: Partial<Omit<LeagueConfig, 'players'>>) {
    Object.assign(config.value, updates)
    save()
  }

  load()

  return { config, addPlayer, removePlayer, movePlayer, updateConfig }
})
