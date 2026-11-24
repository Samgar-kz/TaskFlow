<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AuthLayout from '@/components/AuthLayout.vue'
import { ApiError } from '@/api/http'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const email = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

function useDemo() {
  email.value = 'demo@taskflow.dev'
  password.value = 'Demo123!'
}

async function submit() {
  loading.value = true
  error.value = ''
  try {
    await auth.login(email.value, password.value)
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/') ? route.query.redirect : '/'
    await router.push(redirect)
  } catch (err) {
    error.value = err instanceof ApiError ? err.message : 'Unable to sign in. Please try again.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthLayout title="Welcome back" subtitle="Sign in to your TaskFlow account.">
    <p v-if="route.query.expired" class="mb-4 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
      Your session expired. Please sign in again.
    </p>
    <form class="space-y-4" @submit.prevent="submit">
      <div>
        <label class="label" for="email">Email</label>
        <input id="email" v-model="email" class="input" type="email" autocomplete="email" required />
      </div>
      <div>
        <label class="label" for="password">Password</label>
        <input id="password" v-model="password" class="input" type="password" autocomplete="current-password" required />
      </div>
      <p v-if="error" class="text-sm text-rose-600" role="alert">{{ error }}</p>
      <button class="btn-primary w-full" type="submit" :disabled="loading">{{ loading ? 'Signing in…' : 'Sign in' }}</button>
    </form>

    <button class="btn-secondary mt-3 w-full" type="button" @click="useDemo">Use demo account</button>

    <p class="mt-6 text-center text-sm text-slate-500">
      New to TaskFlow?
      <RouterLink to="/register" class="font-medium text-brand-600 hover:text-brand-700">Create an account</RouterLink>
    </p>
  </AuthLayout>
</template>
