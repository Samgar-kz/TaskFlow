<script lang="ts">
import { ref } from 'vue'

/** Id of the card currently being dragged (shared by all columns). */
const draggingId = ref<string | null>(null)
</script>

<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import type { Column, Member, TaskCard } from '@/api/types'
import AppIcon from '@/components/AppIcon.vue'
import TaskCardView from '@/components/TaskCardView.vue'

const props = defineProps<{
  column: Column
  tasks: TaskCard[]
  members: Map<string, Member>
  isDone: boolean
  canConfigure: boolean
}>()
const emit = defineEmits<{
  move: [taskId: string, columnId: string, index: number]
  open: [task: TaskCard]
  add: [columnId: string]
  configure: [column: Column]
}>()

const list = useTemplateRef<HTMLElement>('list')
const dropIndex = ref<number | null>(null)

const atLimit = computed(() => props.column.wipLimit !== null && props.tasks.length >= props.column.wipLimit)

/** Cards in order; the dragged card keeps its place (dimmed) and has no drop index. */
const entries = computed(() => {
  let index = 0
  return props.tasks.map((task) => ({ task, index: task.id === draggingId.value ? null : index++ }))
})
const othersCount = computed(() => entries.value.filter((e) => e.index !== null).length)

function onDragStart(event: DragEvent) {
  const card = (event.target as HTMLElement).closest<HTMLElement>('[data-task-id]')
  if (!card || !event.dataTransfer) return
  draggingId.value = card.dataset.taskId ?? null
  event.dataTransfer.effectAllowed = 'move'
  event.dataTransfer.setData('text/plain', draggingId.value ?? '')
}

/** Finds the drop index from the pointer position, ignoring the card being dragged. */
function onDragOver(event: DragEvent) {
  if (!draggingId.value || !list.value) return
  event.preventDefault()
  const cards = [...list.value.querySelectorAll<HTMLElement>('[data-task-id]')].filter((el) => el.dataset.taskId !== draggingId.value)
  dropIndex.value = cards.filter((el) => {
    const rect = el.getBoundingClientRect()
    return rect.top + rect.height / 2 < event.clientY
  }).length
}

function onDragLeave(event: DragEvent) {
  if (!list.value?.contains(event.relatedTarget as Node | null)) dropIndex.value = null
}

function onDrop(event: DragEvent) {
  event.preventDefault()
  const id = draggingId.value ?? event.dataTransfer?.getData('text/plain')
  if (id) emit('move', id, props.column.id, dropIndex.value ?? othersCount.value)
  dropIndex.value = null
  draggingId.value = null
}

function onDragEnd() {
  draggingId.value = null
  dropIndex.value = null
}
</script>

<template>
  <section class="flex w-72 shrink-0 flex-col rounded-xl bg-slate-100/80" :aria-label="`${column.name} column`">
    <header class="flex items-center gap-2 px-3 pt-3 pb-2">
      <h3 class="text-sm font-semibold text-slate-700">{{ column.name }}</h3>
      <span
        class="rounded-full px-2 py-0.5 text-xs font-medium"
        :class="atLimit ? 'bg-rose-100 text-rose-700' : 'bg-white text-slate-500'"
        :title="column.wipLimit ? `WIP limit ${column.wipLimit}` : undefined"
      >
        {{ tasks.length }}<template v-if="column.wipLimit"> / {{ column.wipLimit }}</template>
      </span>
      <span v-if="isDone" class="text-xs text-emerald-600">✓</span>
      <span class="flex-1" />
      <button v-if="canConfigure" class="btn-ghost px-1" :aria-label="`Configure ${column.name}`" @click="emit('configure', column)">
        <AppIcon name="settings" :size="16" />
      </button>
    </header>

    <div
      ref="list"
      class="flex min-h-24 flex-1 flex-col gap-2 px-2 pb-2"
      @dragstart="onDragStart"
      @dragend="onDragEnd"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
      @drop="onDrop"
    >
      <template v-for="entry in entries" :key="entry.task.id">
        <div v-if="entry.index !== null && dropIndex === entry.index" class="h-1 rounded-full bg-brand-500" />
        <TaskCardView
          :class="{ 'opacity-40': entry.index === null }"
          :task="entry.task"
          :assignee="entry.task.assigneeId ? members.get(entry.task.assigneeId) : undefined"
          :done="isDone"
          @open="emit('open', $event)"
        />
      </template>
      <div v-if="dropIndex === othersCount && dropIndex !== null" class="h-1 rounded-full bg-brand-500" />
    </div>

    <button class="mx-2 mb-2 flex items-center gap-1 rounded-lg px-2 py-1.5 text-sm text-slate-500 hover:bg-white hover:text-slate-800" @click="emit('add', column.id)">
      <AppIcon name="plus" :size="16" /> Add task
    </button>
  </section>
</template>
