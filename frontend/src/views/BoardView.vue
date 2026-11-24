<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { projectsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Column, TaskCard } from '@/api/types'
import AppIcon from '@/components/AppIcon.vue'
import BaseModal from '@/components/BaseModal.vue'
import BoardColumnView from '@/components/BoardColumnView.vue'
import TaskModal from '@/components/TaskModal.vue'
import UserAvatar from '@/components/UserAvatar.vue'
import { useAuthStore } from '@/stores/auth'
import { useBoardStore } from '@/stores/board'
import { useToastStore } from '@/stores/toast'
import { columnTasks } from '@/utils/boardState'

const props = defineProps<{ id: string }>()
const router = useRouter()
const auth = useAuthStore()
const store = useBoardStore()
const toast = useToastStore()

const openTask = ref<TaskCard | null>(null)
const newTaskColumn = ref<string | null>(null)
const showMembers = ref(false)
const memberEmail = ref('')
const editingColumn = ref<Column | 'new' | null>(null)
const columnForm = reactive({ name: '', wipLimit: '' })
const search = ref('')
const onlyMine = ref(false)

const isOwner = computed(() => store.board?.myRole === 'Owner')
const doneColumnId = computed(() => store.columns.at(-1)?.id)
const visibleTasks = computed(() => {
  const term = search.value.trim().toLowerCase()
  return (store.board?.tasks ?? []).filter(
    (t) =>
      (!term || t.title.toLowerCase().includes(term) || t.key.toLowerCase().includes(term)) &&
      (!onlyMine.value || t.assigneeId === auth.user?.id),
  )
})

const liveLabel = { live: 'Live', connecting: 'Connecting…', reconnecting: 'Reconnecting…', offline: 'Offline' } as const

watch(
  () => props.id,
  async (id) => {
    try {
      await store.load(id)
    } catch (err) {
      toast.error(err)
      await router.replace('/')
    }
  },
  { immediate: true },
)

watch(
  () => store.board,
  async (board, previous) => {
    if (!board && previous) {
      toast.error(new Error('This board is no longer available to you.'))
      await router.replace('/')
    }
  },
)

onBeforeUnmount(() => void store.disconnect())

async function move(taskId: string, columnId: string, index: number) {
  try {
    await store.moveTask(taskId, columnId, index)
  } catch (err) {
    toast.error(err)
  }
}

async function addMember() {
  try {
    store.board = await projectsApi.addMember(props.id, memberEmail.value)
    memberEmail.value = ''
    toast.success('Member added')
  } catch (err) {
    toast.error(err instanceof ApiError && err.status === 404 ? new Error('No TaskFlow user has that email address.') : err)
  }
}

async function removeMember(userId: string) {
  const leaving = userId === auth.user?.id
  if (!window.confirm(leaving ? 'Leave this project?' : 'Remove this member? Their tasks become unassigned.')) return
  try {
    await projectsApi.removeMember(props.id, userId)
    if (leaving) {
      await router.replace('/')
      return
    }
    await store.reload()
  } catch (err) {
    toast.error(err)
  }
}

function configure(column: Column | 'new') {
  editingColumn.value = column
  columnForm.name = column === 'new' ? '' : column.name
  columnForm.wipLimit = column === 'new' || column.wipLimit === null ? '' : String(column.wipLimit)
}

async function saveColumn() {
  const body = { name: columnForm.name, wipLimit: columnForm.wipLimit ? Number(columnForm.wipLimit) : null }
  try {
    store.board =
      editingColumn.value === 'new'
        ? await projectsApi.addColumn(props.id, body)
        : await projectsApi.updateColumn(props.id, editingColumn.value!.id, body)
    editingColumn.value = null
  } catch (err) {
    toast.error(err)
  }
}

async function deleteColumn() {
  if (editingColumn.value === 'new' || !editingColumn.value) return
  try {
    await projectsApi.removeColumn(props.id, editingColumn.value.id)
    await store.reload()
    editingColumn.value = null
  } catch (err) {
    toast.error(err)
  }
}
</script>

<template>
  <div v-if="store.board" class="flex h-full flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <div class="min-w-0 flex-1">
        <div class="flex items-center gap-2">
          <span class="rounded bg-brand-100 px-2 py-0.5 font-mono text-xs font-semibold text-brand-700">{{ store.board.key }}</span>
          <h2 class="truncate text-xl font-semibold text-slate-900">{{ store.board.name }}</h2>
          <span
            class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium"
            :class="store.status === 'live' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700'"
            :title="store.status === 'live' ? 'Changes from teammates appear instantly' : undefined"
          >
            <span class="size-1.5 rounded-full" :class="store.status === 'live' ? 'bg-emerald-500' : 'bg-amber-500'" />
            {{ liveLabel[store.status] }}
          </span>
        </div>
        <p v-if="store.board.description" class="mt-0.5 truncate text-sm text-slate-500">{{ store.board.description }}</p>
      </div>
      <input v-model="search" class="input w-48" type="search" placeholder="Filter tasks" aria-label="Filter tasks" />
      <label class="flex items-center gap-2 text-sm text-slate-600">
        <input v-model="onlyMine" type="checkbox" class="rounded border-slate-300" /> Only mine
      </label>
      <button class="flex -space-x-2" aria-label="Members" @click="showMembers = true">
        <UserAvatar v-for="m in store.board.members.slice(0, 5)" :id="m.userId" :key="m.userId" :name="m.fullName" size="md" />
      </button>
      <button class="btn-secondary" @click="showMembers = true"><AppIcon name="users" :size="16" /> Members</button>
      <button v-if="isOwner" class="btn-secondary" @click="configure('new')"><AppIcon name="columns" :size="16" /> Add column</button>
    </div>

    <div class="-mx-4 flex flex-1 gap-4 overflow-x-auto px-4 pb-4 lg:-mx-8 lg:px-8">
      <BoardColumnView
        v-for="column in store.columns"
        :key="column.id"
        :column="column"
        :tasks="columnTasks(visibleTasks, column.id)"
        :members="store.membersById"
        :is-done="column.id === doneColumnId"
        :can-configure="isOwner"
        @move="move"
        @open="openTask = $event"
        @add="newTaskColumn = $event"
        @configure="configure"
      />
    </div>

    <TaskModal v-if="openTask" :board="store.board" :task="openTask" @close="openTask = null" />
    <TaskModal v-if="newTaskColumn" :board="store.board" :column-id="newTaskColumn" @close="newTaskColumn = null" />

    <BaseModal v-if="showMembers" title="Project members" @close="showMembers = false">
      <ul class="divide-y divide-slate-100">
        <li v-for="m in store.board.members" :key="m.userId" class="flex items-center gap-3 py-2">
          <UserAvatar :id="m.userId" :name="m.fullName" size="md" />
          <div class="min-w-0 flex-1 text-sm">
            <p class="font-medium text-slate-900">{{ m.fullName }} <span v-if="m.role === 'Owner'" class="text-xs text-brand-700">· Owner</span></p>
            <p class="truncate text-slate-500">{{ m.jobTitle }} · {{ m.email }}</p>
          </div>
          <button
            v-if="m.role !== 'Owner' && (isOwner || m.userId === auth.user?.id)"
            class="btn-ghost text-sm text-rose-600"
            @click="removeMember(m.userId)"
          >
            {{ m.userId === auth.user?.id ? 'Leave' : 'Remove' }}
          </button>
        </li>
      </ul>
      <form v-if="isOwner" class="mt-4 flex gap-2" @submit.prevent="addMember">
        <input v-model="memberEmail" class="input" type="email" placeholder="teammate@company.com" aria-label="Email to invite" required />
        <button class="btn-primary" type="submit">Add</button>
      </form>
      <p v-else class="mt-4 text-xs text-slate-500">Only the project owner can add members.</p>
    </BaseModal>

    <BaseModal v-if="editingColumn" :title="editingColumn === 'new' ? 'Add column' : 'Column settings'" @close="editingColumn = null">
      <form class="space-y-4" @submit.prevent="saveColumn">
        <div>
          <label class="label" for="col-name">Name</label>
          <input id="col-name" v-model="columnForm.name" class="input" maxlength="40" required />
        </div>
        <div>
          <label class="label" for="col-wip">WIP limit <span class="font-normal text-slate-400">(optional)</span></label>
          <input id="col-wip" v-model="columnForm.wipLimit" class="input" type="number" min="1" max="100" />
          <p class="mt-1 text-xs text-slate-500">Limits how many tasks can be in this column at once.</p>
        </div>
        <div class="flex items-center gap-2">
          <button v-if="editingColumn !== 'new'" type="button" class="btn-ghost text-rose-600" @click="deleteColumn">Delete column</button>
          <span class="flex-1" />
          <button type="button" class="btn-secondary" @click="editingColumn = null">Cancel</button>
          <button type="submit" class="btn-primary">Save</button>
        </div>
      </form>
    </BaseModal>
  </div>
</template>
