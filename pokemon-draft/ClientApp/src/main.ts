import './assets/main.css'
import 'vuetify/styles'
import '@mdi/font/css/materialdesignicons.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import vue3GoogleLogin from 'vue3-google-login'

import App from './App.vue'
import router from './router'
import { installApiLoadingTracker } from './services/apiLoading'

const vuetify = createVuetify({
  components,
  directives,
  defaults: {
    VTextField: {
      variant: 'outlined',
      density: 'compact',
    },
    VSelect: {
      variant: 'outlined',
      density: 'compact',
    },
    VAutocomplete: {
      variant: 'outlined',
      density: 'compact',
    },
    VNumberInput: {
      variant: 'outlined',
      density: 'compact',
    },
    VTextarea: {
      variant: 'outlined',
      density: 'compact',
    },
  },
  theme: {
    defaultTheme: 'pokeDraftDark',
    themes: {
      pokeDraftDark: {
        dark: true,
        colors: {
          background: '#080b14',
          surface: '#141a2b',
          primary: '#7c6cff',
          secondary: '#ff5c7a',
          success: '#35d39a',
          warning: '#ffca62',
          error: '#ff5c7a',
          info: '#2ab6ff',
        },
      },
    },
  },
  icons: {
    defaultSet: 'mdi',
  },
})

async function bootstrap() {
  const res = await fetch('/api/config')
  const cfg = await res.json()

  installApiLoadingTracker()

  const app = createApp(App)
  app.use(createPinia())
  app.use(router)
  app.use(vuetify)
  app.use(vue3GoogleLogin, { clientId: cfg.googleClientId })
  app.mount('#app')
}

bootstrap()
