<script setup lang="ts">
import { ref } from 'vue';
import { CircleAlert } from '@lucide/vue';
import {
  TooltipContent,
  TooltipPortal,
  TooltipProvider,
  TooltipRoot,
  TooltipTrigger,
} from 'reka-ui';

/**
 * 共用補充說明：label 提供無障礙名稱，說明內容由使用端透過 slot 傳入。
 */
defineProps<{ label: string }>();

// 懸停與鍵盤聚焦交由 Reka 處理，受控狀態另外支援點擊切換，方便觸控使用。
const open = ref(false);
</script>

<template>
  <TooltipProvider :delay-duration="250">
    <TooltipRoot v-model:open="open" disable-closing-trigger>
      <!-- 小型說明按鈕不是表單送出鈕；正常畫面只顯示圖示，不常駐長文字。 -->
      <TooltipTrigger as-child>
        <button
          type="button"
          :aria-label="label"
          class="inline-flex size-6 shrink-0 items-center justify-center rounded text-muted-foreground/80 transition-colors hover:bg-accent hover:text-primary focus-visible:text-primary"
          @click="open = !open"
        >
          <CircleAlert class="size-3.5" aria-hidden="true" />
        </button>
      </TooltipTrigger>

      <!-- 提示移至 body，避免被清單或面板的 overflow 裁切，並由 Reka 處理邊界定位。 -->
      <TooltipPortal>
        <TooltipContent
          side="bottom"
          align="start"
          :side-offset="6"
          :collision-padding="12"
          class="help-hint-content z-50 max-w-[min(360px,calc(100vw-24px))] rounded-md border bg-popover px-3 py-2.5 text-xs leading-relaxed text-popover-foreground shadow-md"
        >
          <slot />
        </TooltipContent>
      </TooltipPortal>
    </TooltipRoot>
  </TooltipProvider>
</template>

<style scoped>
/* 提示內容限制於可用高度，長說明可在浮層內捲動。 */
.help-hint-content {
  max-height: var(--reka-tooltip-content-available-height);
  overflow-y: auto;
  animation: hint-in 120ms ease-out;
}

/* 使用離場動畫讓 Reka 在淡出完成後才移除提示。 */
.help-hint-content[data-state='closed'] {
  animation: hint-out 100ms ease-in;
}

@keyframes hint-in {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

@keyframes hint-out {
  from {
    opacity: 1;
  }
  to {
    opacity: 0;
  }
}

/* 尊重系統減少動態效果設定，不影響說明內容與互動。 */
@media (prefers-reduced-motion: reduce) {
  .help-hint-content {
    animation: none !important;
  }
}
</style>
