import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import vue3GoogleLogin from 'vue3-google-login'

import App from './App.vue'
import router from './router'

async function bootstrap() {
  const res = await fetch('/api/config')
  const cfg = await res.json()

  const app = createApp(App)
  app.use(createPinia())
  app.use(router)
  app.use(vue3GoogleLogin, { clientId: cfg.googleClientId })
  app.mount('#app')
}

bootstrap()
