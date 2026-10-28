<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import {
  Calculator,
  ChartNoAxesCombined,
  ChevronRight,
  Menu,
  PanelLeftClose,
  PanelLeftOpen,
  X,
} from '@lucide/vue';
import {
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
  DialogTrigger,
} from 'reka-ui';
import { Button } from '@/components/ui/button';

/**
 * 全站共用外框，只管理導覽與版面；各功能的資料及操作由內容插槽內的頁面負責。
 */
// 導覽狀態：桌面收合與手機抽屜各自管理，頁面名稱取自路由的 meta.title。
const route = useRoute();
const collapsed = ref(false);
const mobileOpen = ref(false);
const isDesktop = useMediaQuery('(min-width: 768px)');
const pageTitle = computed(() => (route.meta.title as string | undefined) ?? '作業平台');

// 切換功能後關閉手機選單，將操作空間交回目前頁面。
watch(
  () => route.fullPath,
  () => (mobileOpen.value = false),
);

/**
 * 與 md 斷點同步：回到桌面時關閉抽屜，避免遮罩與焦點限制留在背景。
 * 媒體查詢的監聽與卸載清理由 VueUse 管理，不再另外監聽每次 resize。
 */
watch(isDesktop, (desktop) => {
  if (desktop) mobileOpen.value = false;
});
</script>

<template>
  <!-- 手機選單的 Dialog 同時提供焦點限制、Escape 關閉與關閉後的焦點還原。 -->
  <DialogRoot v-model:open="mobileOpen">
    <div class="flex h-dvh flex-col overflow-hidden bg-background text-[13px] text-foreground">
      <!-- 鍵盤導覽入口與全站品牌色帶，不放入各功能頁重複維護。 -->
      <a
        href="#main-content"
        class="sr-only z-50 bg-card px-3 py-2 focus:not-sr-only focus:absolute focus:left-3 focus:top-3"
      >
        跳至主要內容
      </a>
      <div class="grid h-[3px] shrink-0 grid-cols-[6fr_3fr_1fr]" aria-hidden="true">
        <span class="bg-brand-blue"></span>
        <span class="bg-brand-orange"></span>
        <span class="bg-brand-green"></span>
      </div>

      <div class="flex min-h-0 flex-1">
        <!-- 桌面功能選單：固定圖示錨點，僅改變容器寬度與文字透明度。 -->
        <aside
          id="desktop-navigation"
          class="erp-sidebar hidden shrink-0 flex-col overflow-hidden border-r bg-card md:flex"
          :class="collapsed ? 'w-[52px]' : 'w-[180px]'"
          :data-collapsed="collapsed"
          aria-label="系統功能選單"
          data-testid="desktop-sidebar"
        >
          <div class="flex h-11 w-[180px] shrink-0 items-center border-b">
            <span
              class="w-[52px] shrink-0 text-center text-base font-semibold tracking-tight text-primary"
              >KGI</span
            >
            <span
              class="erp-sidebar-copy whitespace-nowrap text-[13px] font-medium"
              :aria-hidden="collapsed"
              >作業平台</span
            >
          </div>
          <nav class="px-2 py-3" aria-label="業務功能">
            <p
              class="erp-sidebar-copy mb-2 whitespace-nowrap px-2 text-[11px] text-muted-foreground"
              :aria-hidden="collapsed"
            >
              業務作業
            </p>
            <RouterLink
              to="/warrants"
              class="flex h-8 items-center gap-2 overflow-hidden rounded-md px-2.5 text-[13px] hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              :class="[
                route.name === 'warrants'
                  ? 'bg-accent font-medium text-accent-foreground'
                  : 'text-muted-foreground',
              ]"
              aria-label="權證試算"
              :title="collapsed ? '權證試算' : undefined"
            >
              <Calculator class="size-4 shrink-0" aria-hidden="true" />
              <span class="erp-sidebar-copy shrink-0 whitespace-nowrap" :aria-hidden="collapsed"
                >權證試算</span
              >
            </RouterLink>
            <RouterLink
              to="/lending"
              class="mt-1 flex h-8 items-center gap-2 overflow-hidden rounded-md px-2.5 text-[13px] hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              :class="
                route.name === 'lending'
                  ? 'bg-accent font-medium text-accent-foreground'
                  : 'text-muted-foreground'
              "
              aria-label="借券維持率監控"
              :title="collapsed ? '借券維持率監控' : undefined"
            >
              <ChartNoAxesCombined class="size-4 shrink-0" aria-hidden="true" />
              <span class="erp-sidebar-copy shrink-0 whitespace-nowrap" :aria-hidden="collapsed"
                >借券維持率監控</span
              >
            </RouterLink>
          </nav>
        </aside>

        <div class="flex min-w-0 flex-1 flex-col">
          <!-- 共用 Header：依螢幕尺寸顯示收合或抽屜按鈕，並呈現目前路由位置。 -->
          <header class="flex h-11 shrink-0 items-center gap-2 border-b bg-card px-3">
            <Button
              variant="ghost"
              size="icon-sm"
              class="hidden rounded-md md:inline-flex"
              :aria-label="collapsed ? '展開系統選單' : '收合系統選單'"
              :aria-expanded="!collapsed"
              aria-controls="desktop-navigation"
              @click="collapsed = !collapsed"
            >
              <PanelLeftOpen v-if="collapsed" class="size-4" aria-hidden="true" />
              <PanelLeftClose v-else class="size-4" aria-hidden="true" />
            </Button>
            <DialogTrigger as-child>
              <Button
                variant="ghost"
                size="icon-sm"
                class="rounded-md md:hidden"
                aria-label="開啟系統選單"
              >
                <Menu class="size-4" aria-hidden="true" />
              </Button>
            </DialogTrigger>
            <nav class="flex min-w-0 items-center gap-1.5 text-xs" aria-label="目前位置">
              <span class="shrink-0 text-muted-foreground">作業平台</span>
              <ChevronRight class="size-3 shrink-0 text-muted-foreground" aria-hidden="true" />
              <span class="truncate font-medium" aria-current="page">{{ pageTitle }}</span>
            </nav>
          </header>
          <!-- 功能工作區：僅承接頁面；清單、表單與資料載入由各 Feature 自行管理。 -->
          <main id="main-content" tabindex="-1" class="min-h-0 flex-1 overflow-auto">
            <slot />
          </main>
        </div>
      </div>
    </div>

    <!-- 手機功能選單：Portal 避免被工作區裁切，與桌面導覽連至相同功能。 -->
    <DialogPortal>
      <DialogOverlay class="erp-drawer-overlay fixed inset-0 z-40 bg-foreground/25" />
      <DialogContent
        class="erp-drawer-content fixed inset-y-0 left-0 z-50 flex w-60 max-w-[85vw] flex-col border-r bg-card text-foreground shadow-lg"
      >
        <div class="flex h-12 shrink-0 items-center justify-between border-b px-3">
          <DialogTitle class="flex items-center gap-2.5 text-[13px] font-medium">
            <span class="text-lg font-semibold tracking-tight text-primary">KGI</span>
            作業平台
          </DialogTitle>
          <DialogClose as-child>
            <Button variant="ghost" size="icon-sm" class="rounded-md" aria-label="關閉系統選單">
              <X class="size-4" aria-hidden="true" />
            </Button>
          </DialogClose>
        </div>
        <DialogDescription class="sr-only"
          >選擇要操作的業務功能，按 Escape 關閉選單。</DialogDescription
        >
        <nav class="px-3 py-3" aria-label="行動版業務功能">
          <p class="mb-2 px-2 text-[11px] text-muted-foreground">業務作業</p>
          <RouterLink
            to="/warrants"
            class="flex min-h-10 items-center gap-2 rounded-md px-2 text-[13px] hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            active-class="bg-accent font-medium text-accent-foreground"
            @click="mobileOpen = false"
          >
            <Calculator class="size-4 shrink-0" aria-hidden="true" />
            權證試算
          </RouterLink>
          <RouterLink
            to="/lending"
            class="mt-1 flex min-h-10 items-center gap-2 rounded-md px-2 text-[13px] hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            active-class="bg-accent font-medium text-accent-foreground"
            @click="mobileOpen = false"
          >
            <ChartNoAxesCombined class="size-4 shrink-0" aria-hidden="true" />
            借券維持率監控
          </RouterLink>
        </nav>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
/* 桌面導覽轉場：保持內容位置穩定，文字在容器收合時淡出。 */
.erp-sidebar {
  transition: width 200ms ease-in-out;
}

.erp-sidebar-copy {
  opacity: 1;
  transition: opacity 150ms ease 50ms;
}

.erp-sidebar[data-collapsed='true'] .erp-sidebar-copy {
  opacity: 0;
  transition-delay: 0ms;
}

/* 手機抽屜轉場：依 Reka 的開關狀態處理遮罩與滑入／滑出，保留離場動畫。 */
.erp-drawer-overlay[data-state='open'] {
  animation: drawer-fade-in 200ms ease-out both;
}

.erp-drawer-overlay[data-state='closed'] {
  animation: drawer-fade-out 180ms ease-in both;
}

.erp-drawer-content[data-state='open'] {
  animation: drawer-slide-in 200ms ease-out both;
}

.erp-drawer-content[data-state='closed'] {
  animation: drawer-slide-out 180ms ease-in both;
}

/* 遮罩與抽屜分別定義進出動畫，交由 Reka 在動畫完成後卸載。 */
@keyframes drawer-fade-in {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

@keyframes drawer-slide-in {
  from {
    transform: translateX(-100%);
  }
  to {
    transform: translateX(0);
  }
}

@keyframes drawer-fade-out {
  from {
    opacity: 1;
  }
  to {
    opacity: 0;
  }
}

@keyframes drawer-slide-out {
  from {
    transform: translateX(0);
  }
  to {
    transform: translateX(-100%);
  }
}

/* 尊重作業系統的減少動態效果偏好，導覽功能仍照常運作。 */
@media (prefers-reduced-motion: reduce) {
  .erp-sidebar,
  .erp-sidebar-copy {
    transition: none;
  }

  .erp-drawer-overlay,
  .erp-drawer-content {
    animation: none !important;
  }
}
</style>
