import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    title?: string
    fullWidth?: boolean
  }
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true, title: 'Sign in' } },
    { path: '/register', name: 'register', component: () => import('@/views/RegisterView.vue'), meta: { public: true, title: 'Create account' } },
    { path: '/', name: 'projects', component: () => import('@/views/ProjectsView.vue'), meta: { title: 'Projects' } },
    { path: '/projects/:id', name: 'board', component: () => import('@/views/BoardView.vue'), props: true, meta: { title: 'Board', fullWidth: true } },
    { path: '/my-tasks', name: 'my-tasks', component: () => import('@/views/MyTasksView.vue'), meta: { title: 'My tasks' } },
    { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('@/views/NotFoundView.vue'), meta: { public: true, title: 'Not found' } },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.restore()

  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: to.fullPath !== '/' ? { redirect: to.fullPath } : {} }
  }
  if ((to.name === 'login' || to.name === 'register') && auth.isAuthenticated) {
    return { name: 'projects' }
  }
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · TaskFlow` : 'TaskFlow'
})

export default router
