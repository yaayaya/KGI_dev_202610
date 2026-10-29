<script setup lang="ts">
import { watch } from 'vue';
import { X } from '@lucide/vue';
import {
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
} from 'reka-ui';
import { Button } from '@/components/ui/button';

/**
 * 股務共用彈窗，沿用 Reka 焦點管理；忙碌時禁止關閉，長內容只捲動彈窗內部。
 */
const props = withDefaults(
  defineProps<{
    open: boolean;
    title: string;
    description?: string;
    busy?: boolean;
    wide?: boolean;
  }>(),
  { description: '', busy: false, wide: false },
);
const emit = defineEmits<{ 'update:open': [value: boolean] }>();
let returnFocus: HTMLElement | null = null;

// 成功操作可能移除原確認按鈕，必須另存入口並在關閉時檢查是否仍可聚焦。
watch(
  () => props.open,
  (open) => {
    if (open)
      returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  },
);

/**
 * 遮罩、Escape 與關閉按鈕共用同一忙碌保護，避免提交途中隱藏結果。
 */
function updateOpen(value: boolean) {
  if (!value && props.busy) return;
  emit('update:open', value);
}
function preventBusyClose(event: Event) {
  if (props.busy) event.preventDefault();
}

/**
 * 原入口被完成狀態取代或停用時，將焦點還給主要內容，避免遺留在關閉的彈窗。
 */
function restoreFocus(event: Event) {
  event.preventDefault();
  if (
    returnFocus?.isConnected &&
    returnFocus !== document.body &&
    !returnFocus.matches(':disabled')
  ) {
    returnFocus.focus({ preventScroll: true });
  } else {
    document.getElementById('main-content')?.focus({ preventScroll: true });
  }
}
</script>

<template>
  <DialogRoot :open="open" @update:open="updateOpen">
    <DialogPortal>
      <!-- 進出動畫交給 Reka 的 data-state，離場結束後才卸載內容。 -->
      <DialogOverlay
        class="fixed inset-0 z-50 bg-foreground/30 duration-150 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=open]:fade-in-0 data-[state=closed]:fade-out-0 motion-reduce:animate-none"
      />
      <DialogContent
        class="fixed top-1/2 left-1/2 z-60 flex max-h-[calc(100dvh-2rem)] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-lg border bg-card text-card-foreground shadow-lg duration-150 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=open]:fade-in-0 data-[state=closed]:fade-out-0 data-[state=open]:zoom-in-95 data-[state=closed]:zoom-out-95 motion-reduce:animate-none"
        :class="wide ? 'max-w-5xl' : 'max-w-lg'"
        :aria-busy="busy"
        @escape-key-down="preventBusyClose"
        @interact-outside="preventBusyClose"
        @close-auto-focus="restoreFocus"
      >
        <header class="flex shrink-0 items-start justify-between gap-3 border-b px-4 py-3">
          <div class="min-w-0">
            <DialogTitle class="text-sm font-semibold">{{ title }}</DialogTitle>
            <DialogDescription
              :class="description ? 'mt-1 text-xs text-muted-foreground' : 'sr-only'"
            >
              {{ description || title }}
            </DialogDescription>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon-xs"
            :disabled="busy"
            :aria-label="`關閉${title}`"
            @click="updateOpen(false)"
          >
            <X class="size-4" aria-hidden="true" />
          </Button>
        </header>
        <!-- 標題與按鈕固定，內容過長時不拉長工作區或移出視窗。 -->
        <div class="min-h-0 flex-1 overflow-auto p-4"><slot /></div>
        <footer
          v-if="$slots.footer"
          class="flex shrink-0 flex-wrap items-center justify-end gap-2 border-t px-4 py-3"
        >
          <slot name="footer" />
        </footer>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
