<script setup lang="ts">
import { computed, ref } from 'vue';
import { CircleCheck, LoaderCircle } from '@lucide/vue';
import type { StockDividendPreview, StockDividendResponse } from '@/api/generated/kgiApi.schemas';
import { Button } from '@/components/ui/button';
import HelpHint from '@/components/HelpHint.vue';
import { formatExact } from './stockAffairsApi';

/**
 * 股利批次與逐戶預覽，只使用後端精確金額、單位與可發放判斷。
 */
const props = defineProps<{
  dividends: StockDividendResponse[];
  preview: StockDividendPreview | null;
  loading: boolean;
  detailLoading: boolean;
  detailError: string;
  busy: boolean;
}>();
const emit = defineEmits<{ preview: [batchId: string]; confirm: [batchId: string] }>();
const requestedId = ref('');
const selectedBatch = computed(() =>
  props.dividends.find((row) => row.batchId === (props.preview?.batchId || requestedId.value)),
);
const completed = computed(() =>
  props.dividends.some((row) => row.batchId === props.preview?.batchId && row.status === 1),
);
const unitLabel = computed(() => (props.preview?.unit === 'SHARE' ? '股' : '元'));

/**
 * 保留選擇以便重試，切換批次時由父層使舊預覽立即失效。
 */
function previewBatch(id: string) {
  requestedId.value = id;
  emit('preview', id);
}
</script>

<template>
  <div class="flex h-full min-h-0 flex-col gap-2 overflow-hidden">
    <!-- 現金與股票批次必須明列單位，避免把配股數誤認為金額。 -->
    <section
      class="erp-panel flex max-h-[42%] min-h-0 shrink-0 flex-col overflow-hidden"
      aria-labelledby="stock-dividends-title"
      :aria-busy="loading"
    >
      <div class="erp-panel-heading shrink-0">
        <h2 id="stock-dividends-title" class="flex items-center gap-1">
          股利發放批次<HelpHint label="股利計算與發放規則"
            >現金逐戶四捨五入至兩位後加總；股票逐戶須為整股。全部明細須與主檔總額完全相符，確認後鎖定。此為模擬發放，不會實際撥款或改變目前持股。</HelpHint
          >
        </h2>
      </div>
      <div class="min-h-0 overflow-auto">
        <table class="erp-table min-w-[800px]">
          <thead class="sticky top-0 z-10">
            <tr>
              <th class="text-left!">批次編號</th>
              <th class="text-left!">類型</th>
              <th>基準日</th>
              <th>每股配發</th>
              <th>預計總額／股數</th>
              <th>狀態</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="batch in dividends"
              :key="batch.batchId"
              :class="preview?.batchId === batch.batchId ? 'bg-accent/50' : ''"
            >
              <td class="text-left! font-medium">{{ batch.batchId }}</td>
              <td class="text-left!">
                {{ batch.dividendType === 'CASH' ? '現金股利' : '股票股利' }}
              </td>
              <td>{{ batch.recordDate }}</td>
              <td>
                {{ batch.dividendPerShare }} {{ batch.unit === 'SHARE' ? '股／股' : '元／股' }}
              </td>
              <td>
                {{ formatExact(batch.totalAmount) }} {{ batch.unit === 'SHARE' ? '股' : '元' }}
              </td>
              <td>
                <span :class="batch.status === 1 ? 'text-success' : 'text-muted-foreground'">{{
                  batch.status === 1 ? '已完成' : '待處理'
                }}</span>
              </td>
              <td>
                <Button
                  type="button"
                  variant="outline"
                  size="xs"
                  :disabled="busy || loading"
                  @click="previewBatch(batch.batchId)"
                  >{{ batch.status === 1 ? '查看發放明細' : '試算' }}</Button
                >
              </td>
            </tr>
            <tr v-if="!dividends.length">
              <td colspan="7" class="h-20! text-center! text-muted-foreground">
                {{ loading ? '載入批次中…' : '尚無股利批次' }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- 明細與主檔精確對帳，無法確認時顯示後端原因並停用按鈕。 -->
    <section
      class="erp-panel flex min-h-0 flex-1 flex-col overflow-hidden"
      aria-labelledby="stock-dividend-preview-title"
      :aria-busy="detailLoading"
    >
      <div class="erp-panel-heading shrink-0 gap-2 py-1.5">
        <h2 id="stock-dividend-preview-title" class="min-w-0 truncate">
          {{ completed ? '已保存發放明細' : '股利試算明細' }}
        </h2>
        <span v-if="completed" class="inline-flex shrink-0 items-center gap-1 text-success"
          ><CircleCheck class="size-3.5" />已完成並鎖定</span
        >
        <Button
          v-else-if="preview"
          type="button"
          size="sm"
          class="shrink-0"
          :disabled="busy || detailLoading || !preview.canConfirm"
          @click="emit('confirm', preview.batchId)"
          ><LoaderCircle v-if="busy" class="size-3.5 animate-spin" />{{
            busy ? '處理中…' : '確認發放'
          }}</Button
        >
      </div>
      <div
        v-if="detailError"
        role="alert"
        class="flex h-12 shrink-0 items-center justify-between gap-2 border-b bg-danger-muted px-3 text-xs text-destructive"
      >
        <p class="line-clamp-2 min-w-0 flex-1 leading-4" :title="detailError">{{ detailError }}</p>
        <Button
          type="button"
          variant="outline"
          size="xs"
          class="shrink-0"
          :disabled="busy || detailLoading || !requestedId"
          @click="previewBatch(requestedId)"
          >重試</Button
        >
      </div>
      <div
        class="flex h-8 shrink-0 items-center gap-4 overflow-x-auto whitespace-nowrap border-b px-3 text-xs text-muted-foreground"
      >
        <span>批次 {{ preview?.batchId || requestedId || '—' }}</span>
        <span>基準日 {{ selectedBatch?.recordDate || '—' }}</span>
        <span>股東 {{ preview ? `${preview.details.length} 人` : '—' }}</span>
      </div>
      <template v-if="preview">
        <div
          class="flex h-9 shrink-0 items-center gap-5 overflow-x-auto whitespace-nowrap border-b px-3 text-xs"
        >
          <p>
            主檔預計
            <strong class="ml-1 tabular-nums"
              >{{ formatExact(preview.totalAmount) }} {{ unitLabel }}</strong
            >
          </p>
          <p>
            明細加總
            <strong
              class="ml-1 tabular-nums"
              :class="preview.matchesExpected ? 'text-success' : 'text-destructive'"
              >{{ formatExact(preview.calculatedTotal) }} {{ unitLabel }}</strong
            >
          </p>
          <span :class="preview.matchesExpected ? 'text-success' : 'text-destructive'">{{
            preview.matchesExpected ? '總額相符' : '總額不符'
          }}</span>
        </div>
        <div
          v-if="preview.validationMessage && !completed"
          :role="preview.canConfirm || completed ? 'status' : 'alert'"
          class="flex h-10 shrink-0 items-center border-b px-3 text-xs"
          :class="
            preview.canConfirm || completed
              ? 'bg-muted text-muted-foreground'
              : 'bg-danger-muted text-destructive'
          "
        >
          <p class="line-clamp-2 leading-4" :title="preview.validationMessage">
            {{ preview.validationMessage }}
          </p>
        </div>
      </template>
      <div class="min-h-0 flex-1 overflow-auto">
        <table class="erp-table min-w-[550px]">
          <thead class="sticky top-0 z-10">
            <tr>
              <th class="text-left!">股東代碼</th>
              <th class="text-left!">姓名</th>
              <th>基準日持股（股）</th>
              <th>配發結果（{{ unitLabel }}）</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="detail in preview?.details ?? []" :key="detail.shareholderId">
              <td class="text-left! font-medium">{{ detail.shareholderId }}</td>
              <td class="text-left!">{{ detail.name }}</td>
              <td>{{ detail.shareQty.toLocaleString('zh-TW') }}</td>
              <td>{{ formatExact(detail.dividendValue) }}</td>
            </tr>
            <tr v-if="!preview?.details.length">
              <td colspan="4" class="h-24! text-center! text-muted-foreground">
                {{
                  detailLoading
                    ? '計算股利明細中…'
                    : preview
                      ? '此批次沒有股利明細'
                      : '選擇批次以查看逐戶試算'
                }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
