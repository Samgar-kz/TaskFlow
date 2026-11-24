<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { tasksApi } from '@/api'
import type { MyTask } from '@/api/types'
import EmptyState from '@/components/EmptyState.vue'
import { useToastStore } from '@/stores/toast'
import { dueState, formatDue, priorityStyle } from '@/utils/format'

const toast = useToastStore()
const tasks = ref<MyTask[] | null>(null)

onMounted(async () => {
  try {
    tasks.value = await tasksApi.mine()
  } catch (err) {
    toast.error(err)
  }
})
</script>

<template>
  <div class="space-y-4">
    <p class="text-sm text-slate-500">Open tasks assigned to you across all projects, soonest due first.</p>
    <EmptyState v-if="tasks && tasks.length === 0" title="Nothing assigned to you" description="Tasks assigned to you on any board show up here." />
    <section v-else class="card overflow-hidden">
      <table class="min-w-full divide-y divide-slate-200">
        <thead class="bg-slate-50">
          <tr>
            <th class="table-head">Task</th>
            <th class="table-head">Project</th>
            <th class="table-head">Status</th>
            <th class="table-head">Priority</th>
            <th class="table-head">Due</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr v-for="t in tasks" :key="t.card.id" class="hover:bg-slate-50">
            <td class="table-cell">
              <span class="mr-2 font-mono text-xs text-slate-500">{{ t.card.key }}</span>
              <span class="font-medium text-slate-900">{{ t.card.title }}</span>
            </td>
            <td class="table-cell"><RouterLink :to="`/projects/${t.projectId}`" class="text-brand-700 hover:underline">{{ t.projectName }}</RouterLink></td>
            <td class="table-cell text-slate-600">{{ t.columnName }}</td>
            <td class="table-cell"><span class="rounded px-1.5 py-0.5 text-xs font-semibold" :class="priorityStyle[t.card.priority]">{{ t.card.priority }}</span></td>
            <td class="table-cell">
              <span
                v-if="t.card.dueDate"
                :class="{
                  'font-medium text-rose-600': dueState(t.card.dueDate) === 'overdue',
                  'font-medium text-amber-600': ['today', 'soon'].includes(dueState(t.card.dueDate)),
                }"
              >
                {{ formatDue(t.card.dueDate) }}
              </span>
              <span v-else class="text-slate-400">—</span>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
  </div>
</template>
