<script setup lang="ts">
import { computed, ref } from 'vue'
import { useTheme } from 'vuetify'
import { applyAppTheme, type AppThemeName } from '@/services/appTheme'

const dialogOpen = ref(false)
const theme = useTheme()

const isDarkMode = computed({
  get: () => theme.global.name.value === 'pokeDraftDark',
  set: (isDark) => {
    const themeName: AppThemeName = isDark ? 'pokeDraftDark' : 'pokeDraftLight'
    theme.global.name.value = themeName
    applyAppTheme(themeName)
  },
})
</script>

<template>
  <v-dialog v-model="dialogOpen" max-width="440">
    <template #activator="{ props }">
      <v-tooltip text="Website settings" location="bottom">
        <template #activator="{ props: tooltipProps }">
          <v-btn
            v-bind="{ ...props, ...tooltipProps }"
            icon="mdi-cog-outline"
            variant="text"
            aria-label="Open website settings"
          />
        </template>
      </v-tooltip>
    </template>

    <v-card class="settings-dialog">
      <v-card-title class="settings-dialog__header">
        <div>
          <span>Website settings</span>
          <small>Appearance</small>
        </div>
        <v-btn
          icon="mdi-close"
          variant="text"
          size="small"
          aria-label="Close website settings"
          @click="dialogOpen = false"
        />
      </v-card-title>

      <v-divider />

      <v-card-text>
        <div class="theme-setting">
          <v-icon :icon="isDarkMode ? 'mdi-weather-night' : 'mdi-white-balance-sunny'" />
          <div class="theme-setting__copy">
            <strong>Dark mode</strong>
            <span>{{ isDarkMode ? 'Dark theme' : 'Light theme' }}</span>
          </div>
          <v-switch
            v-model="isDarkMode"
            color="primary"
            inset
            hide-details
            aria-label="Toggle dark mode"
          />
        </div>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.settings-dialog {
  border-radius: 8px !important;
}

.settings-dialog__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 18px;
}

.settings-dialog__header div,
.settings-dialog__header span,
.settings-dialog__header small {
  display: block;
}

.settings-dialog__header span {
  color: var(--text);
  font-size: 1rem;
  font-weight: 800;
}

.settings-dialog__header small {
  margin-top: 2px;
  color: var(--text-muted);
  font-size: 0.72rem;
}

.theme-setting {
  display: grid;
  grid-template-columns: 32px minmax(0, 1fr) auto;
  align-items: center;
  gap: 10px;
  min-height: 56px;
}

.theme-setting__copy strong,
.theme-setting__copy span {
  display: block;
}

.theme-setting__copy strong {
  color: var(--text);
  font-size: 0.9rem;
  font-weight: 700;
}

.theme-setting__copy span {
  color: var(--text-muted);
  font-size: 0.76rem;
}
</style>