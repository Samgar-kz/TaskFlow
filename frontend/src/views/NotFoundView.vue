<script setup lang="ts">
import { RouterLink } from 'vue-router'
</script>

<template>
  <div class="grid min-h-screen place-items-center p-6 text-center">
    <div>
      <p class="text-sm font-semibold text-brand-600">404</p>
      <h1 class="mt-2 text-3xl font-semibold text-slate-900">Page not found</h1>
      <p class="mt-2 text-slate-500">The page you are looking for doesn't exist.</p>
      <RouterLink to="/" class="btn-primary mt-6">Back to dashboard</RouterLink>
    </div>
  </div>
</template>
