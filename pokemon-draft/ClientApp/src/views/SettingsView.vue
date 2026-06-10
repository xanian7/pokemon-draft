<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { enqueueSnackbar } from '@/services/snackbar'
import PageHeader from '@/components/PageHeader.vue'

const router = useRouter()
const authStore = useAuthStore()

if (!authStore.isAuthenticated) router.replace('/join')

const browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
const teamName = ref(authStore.teamName)
const teamImageUrl = ref(authStore.teamImageUrl)
const timeZone = ref(browserTimeZone || authStore.timeZone)
const isSaving = ref(false)
const imgError = ref(false)

interface AvailabilityDay {
  key: string
  label: string
  enabled: boolean
  start: string
  end: string
}

interface WeeklyAvailability {
  version: 1
  days: Array<Pick<AvailabilityDay, 'key' | 'enabled' | 'start' | 'end'>>
}

const dayDefinitions = [
  { key: 'monday', label: 'Monday' },
  { key: 'tuesday', label: 'Tuesday' },
  { key: 'wednesday', label: 'Wednesday' },
  { key: 'thursday', label: 'Thursday' },
  { key: 'friday', label: 'Friday' },
  { key: 'saturday', label: 'Saturday' },
  { key: 'sunday', label: 'Sunday' },
]

function loadAvailability(): AvailabilityDay[] {
  const defaults = dayDefinitions.map((day) => ({
    ...day,
    enabled: false,
    start: '18:00',
    end: '22:00',
  }))

  if (!authStore.availability) return defaults

  try {
    const saved = JSON.parse(authStore.availability) as Partial<WeeklyAvailability>
    if (saved.version !== 1 || !Array.isArray(saved.days)) return defaults

    return defaults.map((day) => {
      const savedDay = saved.days?.find((item) => item.key === day.key)
      return savedDay
        ? {
            ...day,
            enabled: Boolean(savedDay.enabled),
            start: savedDay.start || day.start,
            end: savedDay.end || day.end,
          }
        : day
    })
  } catch {
    return defaults
  }
}

const availabilityDays = ref<AvailabilityDay[]>(loadAvailability())

const timeZoneItems = (() => {
  const intlWithValues = Intl as typeof Intl & {
    supportedValuesOf?: (key: 'timeZone') => string[]
  }
  const supported = intlWithValues.supportedValuesOf?.('timeZone') ?? [
    'America/New_York',
    'America/Chicago',
    'America/Denver',
    'America/Los_Angeles',
    'America/Phoenix',
    'America/Anchorage',
    'Pacific/Honolulu',
    'UTC',
  ]
  return [...new Set([browserTimeZone, authStore.timeZone, ...supported].filter(Boolean))]
})()

const initials = computed(() => {
  const name = teamName.value.trim() || authStore.playerName || '?'
  return name
    .split(' ')
    .map((word) => word[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
})

const invalidAvailabilityDays = computed(() =>
  availabilityDays.value.filter(
    (day) => day.enabled && (!day.start || !day.end || day.start >= day.end),
  ),
)

function useDetectedTimeZone() {
  timeZone.value = browserTimeZone
}

async function save() {
  if (invalidAvailabilityDays.value.length) {
    enqueueSnackbar('Each available day must end after its start time.', 'error')
    return
  }

  const weeklyAvailability: WeeklyAvailability = {
    version: 1,
    days: availabilityDays.value.map(({ key, enabled, start, end }) => ({
      key,
      enabled,
      start,
      end,
    })),
  }

  isSaving.value = true
  const error = await authStore.updateProfile(
    teamName.value.trim(),
    teamImageUrl.value.trim(),
    timeZone.value,
    JSON.stringify(weeklyAvailability),
  )
  enqueueSnackbar(error ?? 'Team settings saved.', error ? 'error' : 'success')
  isSaving.value = false
}
</script>

<template>
  <v-container fluid class="settings-page">
    <v-card class="settings-card">
      <PageHeader
        class="settings-header"
        eyebrow="Account"
        title="Team Settings"
        subtitle="Manage how your team appears and when other players can schedule matches with you."
      />

      <v-card-text>
        <v-card class="avatar-card mb-2" variant="outlined">
          <v-card-text class="avatar-content">
            <v-avatar size="72" color="primary">
              <v-img
                v-if="teamImageUrl && !imgError"
                :src="teamImageUrl"
                alt="Team avatar"
                cover
                @error="imgError = true"
                @load="imgError = false"
              />
              <span v-else class="text-h6 font-weight-bold">{{ initials }}</span>
            </v-avatar>
            <div>
              <div class="font-weight-bold">Team Avatar</div>
              <div class="text-body-2 text-medium-emphasis">
                Your avatar is shown to other players in the league.
              </div>
            </div>
          </v-card-text>
        </v-card>

        <v-form class="settings-form" @submit.prevent="save">
          <v-text-field
            v-model="teamName"
            label="Team Name"
            maxlength="40"
            :counter="40"
            variant="outlined"
          />

          <v-text-field
            v-model="teamImageUrl"
            label="Team Avatar URL"
            placeholder="https://example.com/avatar.png"
            type="url"
            variant="outlined"
            hint="Paste a direct image URL."
            persistent-hint
          />

          <v-divider />

          <div>
            <div class="text-subtitle-1 font-weight-bold">Match Availability</div>
            <div class="text-body-2 text-medium-emphasis">
              Select the days and local time ranges when you are normally available to play.
            </div>
          </div>

          <div class="timezone-row">
            <v-autocomplete
              v-model="timeZone"
              :items="timeZoneItems"
              label="Time Zone"
              prepend-inner-icon="mdi-clock-outline"
              variant="outlined"
              auto-select-first
              hide-details
            />
            <v-btn
              variant="tonal"
              prepend-icon="mdi-crosshairs-gps"
              @click="useDetectedTimeZone"
            >
              Use detected
            </v-btn>
          </div>
          <v-chip size="small" color="primary" variant="tonal" prepend-icon="mdi-map-marker">
            Detected: {{ browserTimeZone }}
          </v-chip>

          <v-card class="availability-card" variant="outlined">
            <v-list bg-color="transparent">
              <v-list-item
                v-for="day in availabilityDays"
                :key="day.key"
                class="availability-row"
              >
                <div class="day-toggle">
                  <v-checkbox-btn v-model="day.enabled" />
                  <span class="font-weight-medium">{{ day.label }}</span>
                </div>

                <div class="time-range">
                  <v-text-field
                    v-model="day.start"
                    type="time"
                    label="From"
                    density="compact"
                    variant="outlined"
                    hide-details
                    :disabled="!day.enabled"
                    :error="day.enabled && day.start >= day.end"
                  />
                  <span class="text-medium-emphasis">to</span>
                  <v-text-field
                    v-model="day.end"
                    type="time"
                    label="Until"
                    density="compact"
                    variant="outlined"
                    hide-details
                    :disabled="!day.enabled"
                    :error="day.enabled && day.start >= day.end"
                  />
                </div>
              </v-list-item>
            </v-list>
          </v-card>

          <div class="form-actions">
            <v-btn variant="tonal" @click="router.back()">Cancel</v-btn>
            <v-btn
              type="submit"
              color="primary"
              variant="flat"
              prepend-icon="mdi-content-save"
              :loading="isSaving"
            >
              Save Changes
            </v-btn>
          </div>
        </v-form>
      </v-card-text>
    </v-card>
  </v-container>
</template>

<style scoped>
.settings-page {
  padding: clamp(1rem, 2vw, 2rem);
}

.settings-card {
  display: grid;
  grid-template-columns: minmax(260px, 0.7fr) minmax(0, 1.3fr);
  gap: 16px;
  width: 100%;
  padding: 0;
}

.settings-card :deep(.v-card) {
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
}

.settings-header {
  grid-column: 1 / -1;
}

.settings-card > :deep(.v-card-text) {
  display: contents;
}

.avatar-card {
  grid-column: 1;
  grid-row: 3;
  align-self: start;
  background: linear-gradient(145deg, rgba(var(--primary-rgb), 0.12), transparent) !important;
}

.avatar-content {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 14px;
  padding: 24px !important;
}

.settings-form {
  grid-column: 2;
  grid-row: 3;
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 100%;
  padding: 20px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--card-bg-solid);
}

.timezone-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: 8px;
}

.availability-card {
  background: transparent;
}

.availability-row {
  border-bottom: 1px solid var(--border-color);
}

.availability-row:last-child {
  border-bottom: 0;
}

.availability-row :deep(.v-list-item__content) {
  display: grid;
  grid-template-columns: minmax(130px, 0.5fr) minmax(280px, 1fr);
  align-items: center;
  gap: 8px;
  padding: 6px 0;
}

.day-toggle,
.time-range {
  display: flex;
  align-items: center;
}

.day-toggle {
  gap: 4px;
}

.time-range {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  gap: 8px;
  min-width: 0;
}

.time-range :deep(.v-input) {
  min-width: 0;
  width: 100%;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

@media (max-width: 720px) {
  .settings-page {
    padding: 12px;
  }

  .settings-card {
    display: flex;
    flex-direction: column;
  }

  .timezone-row {
    grid-template-columns: 1fr;
  }

  .availability-row :deep(.v-list-item__content) {
    grid-template-columns: 1fr;
    min-width: 0;
  }

  .time-range {
    padding-left: 32px;
  }

  .form-actions {
    width: 100%;
  }

  .form-actions :deep(.v-btn) {
    flex: 1;
  }
}

@media (max-width: 440px) {
  .avatar-content {
    align-items: flex-start;
  }

  .time-range {
    grid-template-columns: 1fr;
  }

  .time-range > span {
    display: none;
  }
}
</style>
