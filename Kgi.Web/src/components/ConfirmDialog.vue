<script setup lang="ts">
import { watch } from 'vue';
import {
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogOverlay,
  AlertDialogPortal,
  AlertDialogRoot,
  AlertDialogTitle,
} from 'reka-ui';
import { Button } from '@/components/ui/button';

/**
 * 新增、修改及刪除共用的確認視窗；僅確認操作，實際寫入與錯誤狀態留在功能頁。
 */
const props = withDefaults(
  defineProps<{
    open: boolean;
    title: string;
    description: string;
    confirmText: string;
    disabled?: boolean;
    destructive?: boolean;
  }>(),
  { disabled: false, destructive: false },
);
const emit = defineEmits<{ 'update:open': [open: boolean]; confirm: [] }>();
let returnFocus: HTMLElement | null = null;

// 頁面可由按鈕或 Enter 開啟視窗，記住實際入口，取消後接回原本的鍵盤操作。
watch(
  () => props.open,
  (open) => {
    if (open && document.activeElement instanceof HTMLElement) returnFocus = document.activeElement;
  },
);

/**
 * 先關閉確認視窗再通知頁面寫入；條件失效時仍可取消，但不得送出。
 */
function confirm() {
  if (props.disabled) return;
  emit('update:open', false);
  emit('confirm');
}

/**
 * 原入口因寫入或輪詢而停用時，將焦點還給主要內容，避免落在已關閉的視窗內。
 */
function restoreFocus(event: Event) {
  event.preventDefault();
  if (returnFocus?.isConnected && !returnFocus.matches(':disabled')) {
    returnFocus.focus({ preventScroll: true });
  } else {
    document.getElementById('main-content')?.focus({ preventScroll: true });
  }
}
</script>

<template>
  <AlertDialogRoot :open="open" @update:open="emit('update:open', $event)">
    <AlertDialogPortal>
      <AlertDialogOverlay class="fixed inset-0 z-40 bg-foreground/30" />
      <AlertDialogContent
        class="fixed top-1/2 left-1/2 z-50 max-h-[calc(100dvh-24px)] w-[min(420px,calc(100vw-24px))] -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-lg border bg-card p-4 text-card-foreground shadow-lg"
        @close-auto-focus="restoreFocus"
      >
        <!-- 先說明本次寫入的影響，功能頁可補上已固定的操作摘要。 -->
        <AlertDialogTitle class="text-sm font-semibold">{{ title }}</AlertDialogTitle>
        <AlertDialogDescription class="mt-2 text-xs leading-relaxed text-muted-foreground">
          {{ description }}
        </AlertDialogDescription>
        <div v-if="$slots.default" class="mt-3 text-xs"><slot /></div>
        <!-- 預設焦點由 AlertDialog 放在取消，避免 Enter 誤觸確認寫入。 -->
        <div class="mt-4 flex justify-end gap-2">
          <AlertDialogCancel as-child>
            <Button type="button" variant="outline" size="sm">取消</Button>
          </AlertDialogCancel>
          <Button
            type="button"
            :variant="destructive ? 'destructive' : 'default'"
            size="sm"
            :disabled="disabled"
            @click="confirm"
          >
            {{ confirmText }}
          </Button>
        </div>
      </AlertDialogContent>
    </AlertDialogPortal>
  </AlertDialogRoot>
</template>
