<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import AppShell from '@/components/AppShell.vue'
import ToastHost from '@/components/ToastHost.vue'

const route = useRoute()
const ready = computed(() => route.matched.length > 0)
const isPublic = computed(() => route.meta.public === true)
</script>

<template>
  <template v-if="ready">
    <RouterView v-if="isPublic" />
    <AppShell v-else>
      <RouterView />
    </AppShell>
  </template>
  <ToastHost />
</template>
