<script setup lang="ts">
import { avatarColor, initials } from '@/utils/format'

defineProps<{ id: string; name: string; size?: 'sm' | 'md' }>()
</script>

<template>
  <span
    class="inline-grid shrink-0 place-items-center rounded-full font-semibold text-white ring-2 ring-white"
    :class="size === 'md' ? 'size-8 text-xs' : 'size-6 text-[10px]'"
    :style="{ backgroundColor: avatarColor(id) }"
    :title="name"
  >
    {{ initials(name) }}
  </span>
</template>
