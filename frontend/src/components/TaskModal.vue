<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import { projectsApi, tasksApi } from '@/api'
import { ApiError } from '@/api/http'
import { PRIORITIES, type Board, type Comment, type TaskCard, type TaskPriority } from '@/api/types'
import BaseModal from '@/components/BaseModal.vue'
import UserAvatar from '@/components/UserAvatar.vue'
import { useBoardStore } from '@/stores/board'
import { useToastStore } from '@/stores/toast'
import { timeAgo } from '@/utils/format'

const props = defineProps<{ board: Board; task?: TaskCard | null; columnId?: string | null }>()
const emit = defineEmits<{ close: [] }>()

const store = useBoardStore()
const toast = useToastStore()

const form = reactive({
  title: props.task?.title ?? '',
  description: '',
  priority: (props.task?.priority ?? 'Medium') as TaskPriority,
  assigneeId: props.task?.assigneeId ?? '',
  dueDate: props.task?.dueDate ?? '',
})
const comments = ref<Comment[]>([])
const newComment = ref('')
const error = ref<ApiError | null>(null)
const saving = ref(false)

onMounted(async () => {
  if (!props.task) return
  const detail = await tasksApi.get(props.task.id)
  form.description = detail.description ?? ''
  comments.value = detail.comments
})

// Comments from teammates arrive in real time.
watch(
  () => store.lastComment,
  (incoming) => {
    if (incoming && incoming.taskId === props.task?.id && !comments.value.some((c) => c.id === incoming.comment.id)) {
      comments.value = [...comments.value, incoming.comment]
    }
  },
)

async function save() {
  saving.value = true
  error.value = null
  const body = {
    title: form.title,
    description: form.description || null,
    priority: form.priority,
    assigneeId: form.assigneeId || null,
    dueDate: form.dueDate || null,
  }
  try {
    const card = props.task
      ? await tasksApi.update(props.task.id, body)
      : await projectsApi.createTask(props.board.id, { ...body, columnId: props.columnId ?? null })
    store.upsert(card)
    toast.success(props.task ? 'Task updated' : `${card.key} created`)
    emit('close')
  } catch (err) {
    if (err instanceof ApiError) error.value = err
    toast.error(err)
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!props.task || !window.confirm(`Delete ${props.task.key}?`)) return
  try {
    await tasksApi.remove(props.task.id)
    store.remove(props.task.id)
    toast.success('Task deleted')
    emit('close')
  } catch (err) {
    toast.error(err)
  }
}

async function addComment() {
  if (!props.task || !newComment.value.trim()) return
  try {
    const comment = await tasksApi.comment(props.task.id, newComment.value)
    if (!comments.value.some((c) => c.id === comment.id)) comments.value = [...comments.value, comment]
    newComment.value = ''
  } catch (err) {
    toast.error(err)
  }
}
</script>

<template>
  <BaseModal :title="task ? `${task.key} · Edit task` : 'New task'" wide @close="emit('close')">
    <form class="space-y-4" novalidate @submit.prevent="save">
      <div>
        <label class="label" for="task-title">Title</label>
        <input id="task-title" v-model="form.title" class="input" maxlength="200" required autofocus />
        <p v-if="error?.field('title')" class="field-error">{{ error.field('title') }}</p>
      </div>
      <div>
        <label class="label" for="task-description">Description</label>
        <textarea id="task-description" v-model="form.description" class="input" rows="4" maxlength="5000" />
      </div>
      <div class="grid gap-4 sm:grid-cols-3">
        <div>
          <label class="label" for="task-priority">Priority</label>
          <select id="task-priority" v-model="form.priority" class="input">
            <option v-for="p in PRIORITIES" :key="p" :value="p">{{ p }}</option>
          </select>
        </div>
        <div>
          <label class="label" for="task-assignee">Assignee</label>
          <select id="task-assignee" v-model="form.assigneeId" class="input">
            <option value="">Unassigned</option>
            <option v-for="m in board.members" :key="m.userId" :value="m.userId">{{ m.fullName }}</option>
          </select>
          <p v-if="error?.field('assigneeId')" class="field-error">{{ error.field('assigneeId') }}</p>
        </div>
        <div>
          <label class="label" for="task-due">Due date</label>
          <input id="task-due" v-model="form.dueDate" class="input" type="date" />
        </div>
      </div>
      <div class="flex items-center gap-2 pt-2">
        <button v-if="task" type="button" class="btn-ghost text-rose-600 hover:bg-rose-50" @click="remove">Delete</button>
        <span class="flex-1" />
        <button type="button" class="btn-secondary" @click="emit('close')">Cancel</button>
        <button type="submit" class="btn-primary" :disabled="saving">{{ task ? 'Save' : 'Create task' }}</button>
      </div>
    </form>

    <section v-if="task" class="mt-6 border-t border-slate-100 pt-4">
      <h3 class="mb-3 text-sm font-semibold text-slate-900">Comments</h3>
      <ul class="max-h-56 space-y-3 overflow-y-auto">
        <li v-for="c in comments" :key="c.id" class="flex gap-2">
          <UserAvatar :id="c.authorId" :name="c.authorName" size="md" />
          <div class="text-sm">
            <p><span class="font-medium text-slate-900">{{ c.authorName }}</span> <span class="text-xs text-slate-500">{{ timeAgo(c.createdAt) }}</span></p>
            <p class="whitespace-pre-line text-slate-700">{{ c.body }}</p>
          </div>
        </li>
        <li v-if="comments.length === 0" class="text-sm text-slate-500">No comments yet.</li>
      </ul>
      <form class="mt-3 flex gap-2" @submit.prevent="addComment">
        <input v-model="newComment" class="input" maxlength="2000" placeholder="Write a comment…" aria-label="Comment" />
        <button class="btn-primary" type="submit" :disabled="!newComment.trim()">Post</button>
      </form>
    </section>
  </BaseModal>
</template>
