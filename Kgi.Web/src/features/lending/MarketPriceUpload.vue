<script setup lang="ts">
import { computed, ref, toRef } from 'vue';
import { Eye, FileUp, LoaderCircle, Upload, X } from '@lucide/vue';
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
import HelpHint from '@/components/HelpHint.vue';
import { useCsvPreview } from './useCsvPreview';

/**
 * 行情選檔與可選預覽；只傳出原始檔案，正式內容驗證及寫入由父層與 API 負責。
 */
const props = defineProps<{
  file: File | null;
  fileError: string;
  importError: string;
  notice: string;
  importing: boolean;
  canImport: boolean;
}>();
const emit = defineEmits<{ select: [files: File[]]; import: [] }>();

// 拖曳深度可避免游標經過子元素時反覆清掉框線；選檔取消不影響目前檔案。
const fileInput = ref<HTMLInputElement | null>(null);
const dragDepth = ref(0);
const dragging = computed(() => dragDepth.value > 0 && !props.importing);
const preview = useCsvPreview(toRef(props, 'file'));
const { open, loading: previewLoading, error: previewError, rows } = preview;
const fileSize = computed(() => (props.file ? `${(props.file.size / 1024).toFixed(1)} KiB` : ''));

/**
 * 清除原生 input 值，讓相同檔名修正後也能重新選取。
 */
function onFileChange(event: Event) {
  const input = event.target as HTMLInputElement;
  const files = Array.from(input.files ?? []);
  if (files.length) emit('select', files);
  input.value = '';
}

/**
 * 僅接受檔案拖曳，拖放或選檔期間維持同一套驗證與上傳鎖定規則。
 */
function onDragEnter(event: DragEvent) {
  if (event.dataTransfer?.types.includes('Files')) dragDepth.value += 1;
}

function onDrop(event: DragEvent) {
  dragDepth.value = 0;
  if (!props.importing && event.dataTransfer?.types.includes('Files')) {
    emit('select', Array.from(event.dataTransfer.files));
  }
}
</script>

<template>
  <DialogRoot v-model:open="open">
    <section
      class="erp-panel shrink-0"
      aria-labelledby="market-price-upload-title"
      :aria-busy="importing"
    >
      <!-- 匯入區只呈現選檔摘要，預覽由按鈕另外開啟，避免占用監控表格空間。 -->
      <div class="erp-panel-heading">
        <h2 id="market-price-upload-title" class="flex items-center gap-1.5">
          <FileUp class="size-3.5 text-muted-foreground" aria-hidden="true" />CSV 行情匯入
          <HelpHint label="行情 CSV 格式與匯入規則">
            <p>欄位依序為 Stock_ID、Closing_Price、Trade_Date，交易日格式為 yyyyMMdd。</p>
            <p class="mt-1">
              使用 UTF-8、單一交易日，最多 10,000 筆、2
              MiB，須包含所有合約標的。同日匯入會整批取代該日行情。
            </p>
          </HelpHint>
        </h2>
        <span class="text-[11px] font-normal text-muted-foreground">單一交易日 · 最多 2 MiB</span>
      </div>
      <input
        ref="fileInput"
        type="file"
        accept=".csv,text/csv"
        class="hidden"
        :disabled="importing"
        aria-label="選擇行情 CSV 檔案"
        @change="onFileChange"
      />
      <div class="p-3">
        <div
          class="flex flex-wrap items-center gap-3 rounded-md border border-dashed px-3 py-3 transition-colors"
          :class="dragging ? 'border-primary bg-accent' : 'border-input/50 bg-muted/20'"
          @dragenter.prevent="onDragEnter"
          @dragover.prevent
          @dragleave.prevent="dragDepth = Math.max(0, dragDepth - 1)"
          @drop.prevent.stop="onDrop"
        >
          <Upload class="size-5 shrink-0 text-primary" aria-hidden="true" />
          <div class="min-w-0 flex-1" aria-live="polite">
            <p v-if="file" class="truncate text-[13px] font-medium" :title="file.name">
              {{ file.name }}
            </p>
            <p v-else class="text-[13px]">拖曳 CSV 至此，或點選檔案</p>
            <p class="mt-0.5 text-[11px] text-muted-foreground">
              {{ file ? `${fileSize} · 尚未匯入` : 'Stock_ID,Closing_Price,Trade_Date' }}
            </p>
          </div>
          <div class="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              :disabled="importing"
              @click="fileInput?.click()"
            >
              {{ file ? '重新選擇' : '選擇檔案' }}
            </Button>
            <DialogTrigger as-child>
              <Button type="button" variant="outline" size="sm" :disabled="!file || importing">
                <Eye class="size-3.5" aria-hidden="true" />查看預覽
              </Button>
            </DialogTrigger>
            <Button
              type="button"
              size="sm"
              :disabled="!canImport || !!previewError || previewLoading"
              @click="emit('import')"
            >
              <LoaderCircle v-if="importing" class="size-3.5 animate-spin" aria-hidden="true" />
              {{ importing ? '匯入中…' : '確認匯入' }}
            </Button>
          </div>
        </div>

        <!-- 檔案格式、正式匯入及刷新結果分開呈現，避免將已成功寫入誤認為失敗。 -->
        <p
          v-if="fileError || previewError"
          class="mt-2 whitespace-pre-line text-xs text-destructive"
          role="alert"
        >
          {{ fileError || previewError }}
        </p>
        <p
          v-if="importError"
          class="mt-2 whitespace-pre-line text-xs text-destructive"
          role="alert"
        >
          {{ importError }}
        </p>
        <p v-if="notice" class="mt-2 text-xs text-success" role="status">{{ notice }}</p>
      </div>
    </section>

    <!-- 彈窗沿用 Reka 的焦點限制、Escape 與關閉後焦點還原；關閉不移除選檔。 -->
    <DialogPortal>
      <DialogOverlay class="erp-dialog-overlay fixed inset-0 z-40 bg-foreground/30" />
      <DialogContent
        class="erp-dialog-content fixed top-1/2 left-1/2 z-50 flex max-h-[85dvh] w-[min(640px,calc(100vw-24px))] -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-lg border bg-card text-card-foreground shadow-lg"
      >
        <div class="flex items-center justify-between gap-3 border-b px-4 py-3">
          <DialogTitle class="text-sm font-semibold">CSV 行情預覽</DialogTitle>
          <DialogClose as-child>
            <Button type="button" variant="ghost" size="icon-sm" aria-label="關閉 CSV 預覽"
              ><X class="size-4" aria-hidden="true"
            /></Button>
          </DialogClose>
        </div>
        <div class="min-w-0 px-4 py-3">
          <p class="truncate text-xs font-medium" :title="file?.name">{{ file?.name }}</p>
          <DialogDescription class="mt-1 text-xs text-muted-foreground"
            >僅預覽前 10 筆原始資料，尚未寫入；完整檔案會在確認匯入時驗證。</DialogDescription
          >
        </div>
        <div class="min-h-0 overflow-auto border-y">
          <p
            v-if="previewLoading"
            class="flex h-28 items-center justify-center gap-2 text-xs text-muted-foreground"
            role="status"
          >
            <LoaderCircle class="size-4 animate-spin" aria-hidden="true" />讀取預覽中
          </p>
          <p v-else-if="previewError" class="px-4 py-5 text-xs text-destructive" role="alert">
            {{ previewError }}
          </p>
          <table v-else class="erp-table min-w-[420px]" aria-label="CSV 前十筆行情預覽">
            <thead>
              <tr>
                <th scope="col">筆次</th>
                <th scope="col" class="text-left!">股票代碼</th>
                <th scope="col">收盤價（元）</th>
                <th scope="col">交易日期</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(row, index) in rows" :key="index">
                <td class="text-muted-foreground">{{ index + 1 }}</td>
                <td class="text-left! font-medium">{{ row.Stock_ID }}</td>
                <td>{{ row.Closing_Price }}</td>
                <td>{{ row.Trade_Date }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="flex justify-end px-4 py-3">
          <DialogClose as-child
            ><Button type="button" variant="outline" size="sm">關閉預覽</Button></DialogClose
          >
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
