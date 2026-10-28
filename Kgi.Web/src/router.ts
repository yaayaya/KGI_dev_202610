import { defineComponent, h } from 'vue';
import { createRouter, createWebHistory, RouterLink } from 'vue-router';

/**
 * 未知網址顯示獨立的 404 畫面，保留錯誤位置並提供回到現有功能的入口。
 */
const NotFoundPage = defineComponent({
  name: 'NotFoundPage',
  setup: () => () =>
    h('section', { class: 'p-4' }, [
      h('h1', { class: 'text-base font-semibold' }, '找不到頁面'),
      h('p', { class: 'mt-2 text-sm text-muted-foreground' }, '請確認網址，或由左側選單開啟功能。'),
      h(
        RouterLink,
        {
          to: '/warrants',
          class: 'mt-3 inline-block text-sm text-primary underline underline-offset-4',
        },
        () => '前往權證試算',
      ),
    ]),
});

/**
 * 功能路由與頁面名稱的入口；meta.title 同時供共用 Header 顯示目前位置。
 */
export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    // 目前僅提供 Q1，根網址直接進入權證試算。
    { path: '/', redirect: '/warrants' },
    // 業務頁採延後載入，新增功能時在此登記對應的 Feature 與頁面標題。
    {
      path: '/warrants',
      name: 'warrants',
      component: () => import('./features/warrants/WarrantsPage.vue'),
      meta: { title: '權證試算' },
    },
    // 最後才處理未匹配的路徑，避免攔截已登記的功能頁。
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: NotFoundPage,
      meta: { title: '找不到頁面' },
    },
  ],
});
