<script setup lang="ts">
import { computed } from 'vue'
import type { Member, TaskCard } from '@/api/types'
import AppIcon from '@/components/AppIcon.vue'
import UserAvatar from '@/components/UserAvatar.vue'
import { dueState, formatDue, priorityStyle } from '@/utils/format'

const props = defineProps<{ task: TaskCard; assignee?: Member; done?: boolean }>()
defineEmits<{ open: [task: TaskCard] }>()

const due = computed(() => (props.task.dueDate && !props.done ? dueState(props.task.dueDate) : null))
</script>

<template>
  <article
    class="group cursor-grab rounded-lg border border-slate-200 bg-white p-3 shadow-xs transition hover:border-brand-500 hover:shadow-sm active:cursor-grabbing"
    :class="{ 'opacity-70': done }"
    draggable="true"
    :data-task-id="task.id"
    tabindex="0"
    @click="$emit('open', task)"
    @keydown.enter="$emit('open', task)"
  >
    <div class="mb-1 flex items-center justify-between gap-2">
      <span class="font-mono text-[11px] text-slate-500" :class="{ 'line-through': done }">{{ task.key }}</span>
      <span class="rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase" :class="priorityStyle[task.priority]">{{ task.priority }}</span>
    </div>
    <p class="text-sm font-medium text-slate-900">{{ task.title }}</p>
    <div class="mt-2 flex items-center gap-2 text-xs text-slate-500">
      <span
        v-if="task.dueDate"
        class="inline-flex items-center gap-1"
        :class="{ 'font-medium text-rose-600': due === 'overdue', 'font-medium text-amber-600': due === 'today' || due === 'soon' }"
      >
        <AppIcon name="calendar" :size="12" />{{ formatDue(task.dueDate) }}
      </span>
      <span v-if="task.commentCount" class="inline-flex items-center gap-1"><AppIcon name="chat" :size="12" />{{ task.commentCount }}</span>
      <span class="flex-1" />
      <UserAvatar v-if="assignee" :id="assignee.userId" :name="assignee.fullName" />
    </div>
  </article>
</template>
