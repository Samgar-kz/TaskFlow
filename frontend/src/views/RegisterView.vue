<script setup lang="ts">
import { reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AuthLayout from '@/components/AuthLayout.vue'
import { ApiError } from '@/api/http'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()

const form = reactive({ fullName: '', email: '', password: '', jobTitle: '' })
const error = ref<ApiError | null>(null)
const loading = ref(false)

async function submit() {
  loading.value = true
  error.value = null
  try {
    await auth.register(form)
    await router.push('/')
  } catch (err) {
    error.value = err instanceof ApiError ? err : new ApiError(0, 'Unable to create the account.')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthLayout title="Create your account" subtitle="Start tracking your finances in a minute.">
    <form class="space-y-4" novalidate @submit.prevent="submit">
      <div>
        <label class="label" for="fullName">Full name</label>
        <input id="fullName" v-model="form.fullName" class="input" autocomplete="name" required />
        <p v-if="error?.field('fullName')" class="field-error">{{ error.field('fullName') }}</p>
      </div>
      <div>
        <label class="label" for="email">Email</label>
        <input id="email" v-model="form.email" class="input" type="email" autocomplete="email" required />
        <p v-if="error?.field('email')" class="field-error">{{ error.field('email') }}</p>
      </div>
      <div>
        <label class="label" for="password">Password</label>
        <input id="password" v-model="form.password" class="input" type="password" autocomplete="new-password" required />
        <p class="mt-1 text-xs text-slate-500">At least 8 characters with upper- and lowercase letters and a digit.</p>
        <p v-if="error?.field('password')" class="field-error">{{ error.field('password') }}</p>
      </div>
      <div>
        <label class="label" for="jobTitle">Job title</label>
        <input id="jobTitle" v-model="form.jobTitle" class="input" placeholder="e.g. Frontend Developer" required />
        <p v-if="error?.field('jobTitle')" class="field-error">{{ error.field('jobTitle') }}</p>
      </div>
      <p v-if="error && Object.keys(error.fieldErrors).length === 0" class="text-sm text-rose-600" role="alert">
        {{ error.message }}
      </p>
      <button class="btn-primary w-full" type="submit" :disabled="loading">
        {{ loading ? 'Creating account…' : 'Create account' }}
      </button>
    </form>
    <p class="mt-6 text-center text-sm text-slate-500">
      Already have an account?
      <RouterLink to="/login" class="font-medium text-brand-600 hover:text-brand-700">Sign in</RouterLink>
    </p>
  </AuthLayout>
</template>
