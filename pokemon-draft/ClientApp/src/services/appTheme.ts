export const APP_THEME_STORAGE_KEY = 'pokemon-draft:theme'

export type AppThemeName = 'pokeDraftDark' | 'pokeDraftLight'

export function getStoredAppTheme(): AppThemeName {
  return localStorage.getItem(APP_THEME_STORAGE_KEY) === 'pokeDraftLight'
    ? 'pokeDraftLight'
    : 'pokeDraftDark'
}

export function applyAppTheme(themeName: AppThemeName) {
  localStorage.setItem(APP_THEME_STORAGE_KEY, themeName)
  document.documentElement.dataset.appTheme =
    themeName === 'pokeDraftLight' ? 'light' : 'dark'
}