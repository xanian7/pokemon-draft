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
