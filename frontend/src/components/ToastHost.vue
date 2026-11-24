<script setup lang="ts">
import { useToastStore } from '@/stores/toast'

const toast = useToastStore()
</script>

<template>
  <div class="pointer-events-none fixed right-4 bottom-4 z-[60] flex w-80 flex-col gap-2" role="status" aria-live="polite">
    <TransitionGroup
      enter-from-class="translate-y-2 opacity-0"
      enter-active-class="transition"
      leave-to-class="opacity-0"
      leave-active-class="transition"
    >
      <div
        v-for="item in toast.toasts"
        :key="item.id"
        class="pointer-events-auto rounded-lg px-4 py-3 text-sm font-medium shadow-lg"
        :class="item.kind === 'success' ? 'bg-slate-900 text-white' : 'bg-rose-600 text-white'"
        @click="toast.dismiss(item.id)"
      >
        {{ item.message }}
      </div>
    </TransitionGroup>
  </div>
</template>
