<script setup lang="ts">
import { LoaderCircle, Play } from '@lucide/vue';
import type { InterestBatchResponse } from '@/api/generated/kgiApi.schemas';
import { Button } from '@/components/ui/button';

/**
 * 結算批次表負責選取、進度及按鈕呈現；狀態與金額直接沿用後端快照。
 */
defineProps<{
  batches: InterestBatchResponse[];
  selectedBatchId: string;
  startingIds: Set<string>;
  unknownIds: Set<string>;
  loading: boolean;
  failed: boolean;
}>();
const emit = defineEmits<{ select: [batchId: string]; start: [batchId: string] }>();
const statusLabels: Record<number, string> = { 0: '待處理', 1: '處理中', 2: '結算成功', 9: '失敗' };

/**
 * 失敗進度保留實際百分比，不以完成色彩或強制滿格掩蓋中斷位置。
 */
function statusClass(status: number) {
  if (status === 9) return 'bg-danger-muted text-destructive';
  if (status === 2) return 'bg-success-muted text-success';
  if (status === 1) return 'bg-accent text-primary';
  return 'bg-muted text-muted-foreground';
}
</script>

<template>
  <section
    class="erp-panel overflow-hidden"
    aria-labelledby="interest-batches-title"
    :aria-busy="loading"
  >
    <!-- 輪詢只更新同一筆資料，不重設選取或表格捲動位置。 -->
    <div class="erp-panel-heading">
      <h2 id="interest-batches-title">結算批次</h2>
      <span class="text-[11px] font-normal text-muted-foreground">共 {{ batches.length }} 批</span>
    </div>
    <div class="max-h-[46vh] overflow-auto">
      <table
        class="erp-table min-w-[660px] border-separate border-spacing-0"
        aria-label="利息結算批次與進度"
      >
        <thead class="sticky top-0 z-10 bg-muted [&_th]:bg-muted">
          <tr>
            <th scope="col" class="text-left!">批次編號</th>
            <th scope="col">預計總額（元）</th>
            <th scope="col" class="text-center!">狀態</th>
            <th scope="col" class="text-left!">結算進度</th>
            <th scope="col" class="text-center!">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="batch in batches"
            :key="batch.batchId"
            :class="{ 'bg-accent/50': batch.batchId === selectedBatchId }"
          >
            <td class="text-left!">
              <button
                type="button"
                class="min-h-9 text-left font-medium text-primary underline-offset-4 hover:underline md:min-h-7"
                :aria-pressed="batch.batchId === selectedBatchId"
                :aria-label="`查看 ${batch.batchId} 結算明細`"
                @click="emit('select', batch.batchId)"
              >
                {{ batch.batchId }}
              </button>
            </td>
            <td>{{ batch.expectedAmt }}</td>
            <td class="text-center!">
              <span
                class="inline-flex rounded px-1.5 py-0.5 text-[11px] font-medium"
                :class="statusClass(batch.processStatus)"
              >
                {{ statusLabels[batch.processStatus] ?? '未知狀態' }}
              </span>
            </td>
            <td>
              <div class="flex items-center gap-2">
                <progress
                  class="interest-progress h-2 w-28"
                  :class="
                    batch.processStatus === 9
                      ? 'text-destructive'
                      : batch.processStatus === 2
                        ? 'text-success'
                        : 'text-primary'
                  "
                  :value="batch.progressRate"
                  max="100"
                  :aria-label="`${batch.batchId} 結算進度`"
                >
                  {{ batch.progressRate }}%
                </progress>
                <span class="w-9 text-right tabular-nums">{{ batch.progressRate }}%</span>
              </div>
            </td>
            <td class="text-center!">
              <Button
                type="button"
                variant="outline"
                size="sm"
                class="my-1 min-h-9 min-w-24 md:min-h-7"
                :disabled="
                  batch.processStatus !== 0 ||
                  startingIds.has(batch.batchId) ||
                  unknownIds.has(batch.batchId)
                "
                :aria-label="`${batch.batchId} 啟動結算`"
                @click="emit('start', batch.batchId)"
              >
                <LoaderCircle
                  v-if="
                    startingIds.has(batch.batchId) ||
                    batch.processStatus === 1 ||
                    unknownIds.has(batch.batchId)
                  "
                  class="size-3.5 animate-spin"
                  aria-hidden="true"
                />
                <Play v-else-if="batch.processStatus === 0" class="size-3.5" aria-hidden="true" />
                {{
                  startingIds.has(batch.batchId)
                    ? '送出中…'
                    : unknownIds.has(batch.batchId)
                      ? '確認中…'
                      : batch.processStatus === 1
                        ? '結算中…'
                        : batch.processStatus === 2
                          ? '已完成'
                          : batch.processStatus === 9
                            ? '已失敗'
                            : '啟動結算'
                }}
              </Button>
            </td>
          </tr>
          <tr v-if="!batches.length">
            <td colspan="5" class="h-24! text-center! text-muted-foreground">
              <span v-if="loading" class="inline-flex items-center gap-2" role="status"
                ><LoaderCircle
                  class="size-3.5 animate-spin"
                  aria-hidden="true"
                />載入結算批次中</span
              >
              <span v-else-if="failed">批次清單暫時無法顯示，請重新整理。</span>
              <span v-else>目前沒有結算批次。</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
/* 原生進度條保留輔助工具語意，顏色沿用既有主題與業務狀態。 */
.interest-progress {
  appearance: none;
  overflow: hidden;
  border: 0;
  border-radius: 999px;
  background: var(--muted);
}
.interest-progress::-webkit-progress-bar {
  background: var(--muted);
}
.interest-progress::-webkit-progress-value {
  background: currentColor;
}
.interest-progress::-moz-progress-bar {
  background: currentColor;
}
</style>
