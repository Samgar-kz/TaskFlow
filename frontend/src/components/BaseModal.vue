<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import AppIcon from '@/components/AppIcon.vue'

defineProps<{ title: string; wide?: boolean }>()
const emit = defineEmits<{ close: [] }>()

function onKey(event: KeyboardEvent) {
  if (event.key === 'Escape') emit('close')
}

onMounted(() => window.addEventListener('keydown', onKey))
onBeforeUnmount(() => window.removeEventListener('keydown', onKey))
</script>

<template>
  <Teleport to="body">
    <div class="fixed inset-0 z-50 flex items-end justify-center bg-slate-900/50 p-4 sm:items-center" @click.self="emit('close')">
      <div class="card w-full p-6" :class="wide ? 'max-w-2xl' : 'max-w-lg'" role="dialog" aria-modal="true" :aria-label="title">
        <div class="mb-5 flex items-center justify-between">
          <h2 class="text-lg font-semibold text-slate-900">{{ title }}</h2>
          <button class="btn-ghost" aria-label="Close" @click="emit('close')">
            <AppIcon name="close" />
          </button>
        </div>
        <slot />
      </div>
    </div>
  </Teleport>
</template>
