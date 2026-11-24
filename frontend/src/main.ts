import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import { installAuth } from './stores/auth'

const app = createApp(App)

app.use(createPinia())
installAuth(() => {
  void router.push({ name: 'login', query: { expired: '1' } })
})
app.use(router)

app.mount('#app')
