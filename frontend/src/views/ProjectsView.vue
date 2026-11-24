<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { projectsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { ProjectSummary } from '@/api/types'
import AppIcon from '@/components/AppIcon.vue'
import BaseModal from '@/components/BaseModal.vue'
import EmptyState from '@/components/EmptyState.vue'
import { useToastStore } from '@/stores/toast'

const router = useRouter()
const toast = useToastStore()

const projects = ref<ProjectSummary[] | null>(null)
const showCreate = ref(false)
const keyEdited = ref(false)
const form = reactive({ name: '', key: '', description: '' })
const error = ref<ApiError | null>(null)

/** Suggests a key from the name: "Website Redesign" → "WR", "Payments" → "PAY". */
function suggestKey(name: string): string {
  const words = name.toUpperCase().replace(/[^A-Z0-9 ]/g, '').split(' ').filter(Boolean)
  const key = words.length > 1 ? words.map((w) => w[0]).join('') : (words[0] ?? '').slice(0, 3)
  return key.slice(0, 10)
}

watch(
  () => form.name,
  (name) => {
    if (!keyEdited.value) form.key = suggestKey(name)
  },
)

async function create() {
  error.value = null
  try {
    const board = await projectsApi.create({ name: form.name, key: form.key, description: form.description || null })
    await router.push(`/projects/${board.id}`)
  } catch (err) {
    if (err instanceof ApiError) error.value = err
    toast.error(err)
  }
}

onMounted(async () => {
  try {
    projects.value = await projectsApi.list()
  } catch (err) {
    toast.error(err)
  }
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between">
      <p class="text-sm text-slate-500">Boards you own or were invited to.</p>
      <button class="btn-primary" @click="showCreate = true"><AppIcon name="plus" :size="16" /> New project</button>
    </div>

    <EmptyState v-if="projects && projects.length === 0" title="No projects yet" description="Create a board and invite your team.">
      <button class="btn-primary" @click="showCreate = true">Create project</button>
    </EmptyState>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <RouterLink
        v-for="p in projects"
        :key="p.id"
        :to="`/projects/${p.id}`"
        class="card block p-5 transition hover:border-brand-500 hover:shadow-md"
      >
        <div class="flex items-center gap-2">
          <span class="rounded bg-brand-100 px-2 py-0.5 font-mono text-xs font-semibold text-brand-700">{{ p.key }}</span>
          <span v-if="p.myRole === 'Owner'" class="text-xs text-slate-500">Owner</span>
        </div>
        <h3 class="mt-2 font-semibold text-slate-900">{{ p.name }}</h3>
        <p class="mt-1 line-clamp-2 min-h-10 text-sm text-slate-500">{{ p.description ?? 'No description' }}</p>
        <div class="mt-4 h-1.5 overflow-hidden rounded-full bg-slate-100">
          <div
            class="h-full rounded-full bg-emerald-500"
            :style="{ width: `${p.openTasks + p.doneTasks ? Math.round((p.doneTasks / (p.openTasks + p.doneTasks)) * 100) : 0}%` }"
          />
        </div>
        <div class="mt-2 flex justify-between text-xs text-slate-500">
          <span>{{ p.doneTasks }} done · {{ p.openTasks }} open</span>
          <span class="inline-flex items-center gap-1"><AppIcon name="users" :size="14" /> {{ p.memberCount }}</span>
        </div>
      </RouterLink>
    </div>

    <BaseModal v-if="showCreate" title="New project" @close="showCreate = false">
      <form class="space-y-4" novalidate @submit.prevent="create">
        <div>
          <label class="label" for="p-name">Name</label>
          <input id="p-name" v-model="form.name" class="input" maxlength="80" placeholder="e.g. Website Redesign" required />
          <p v-if="error?.field('name')" class="field-error">{{ error.field('name') }}</p>
        </div>
        <div>
          <label class="label" for="p-key">Key</label>
          <input
            id="p-key"
            v-model="form.key"
            class="input font-mono uppercase"
            maxlength="10"
            placeholder="WEB"
            @input="keyEdited = true"
          />
          <p class="mt-1 text-xs text-slate-500">Prefix for task keys, e.g. {{ form.key || 'WEB' }}-12.</p>
          <p v-if="error?.field('key')" class="field-error">{{ error.field('key') }}</p>
        </div>
        <div>
          <label class="label" for="p-desc">Description <span class="font-normal text-slate-400">(optional)</span></label>
          <textarea id="p-desc" v-model="form.description" class="input" rows="2" maxlength="1000" />
        </div>
        <div class="flex justify-end gap-2">
          <button type="button" class="btn-secondary" @click="showCreate = false">Cancel</button>
          <button type="submit" class="btn-primary">Create</button>
        </div>
      </form>
    </BaseModal>
  </div>
</template>
