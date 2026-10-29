<script setup lang="ts">
import { LoaderCircle, RefreshCw } from '@lucide/vue';
import { toast } from 'vue-sonner';
import { Button } from '@/components/ui/button';
import HelpHint from '@/components/HelpHint.vue';
import InterestBatchTable from './InterestBatchTable.vue';
import { useInterestMonitor } from './useInterestMonitor';

/**
 * Q3 操作與監控共用同一頁；批次啟動、五秒輪詢及錯誤恢復由功能 composable 管理。
 */
const {
  batches,
  selectedBatchId,
  selectedBatch,
  selectedDetail,
  refreshing,
  initialLoading,
  listError,
  detailError,
  startError,
  lastUpdatedTime,
  startingIds,
  unknownIds,
  reloadMonitor,
  selectBatch,
  startSettlement,
} = useInterestMonitor();
const timeFormatter = new Intl.DateTimeFormat('sv-SE', {
  timeZone: 'Asia/Taipei',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});

/**
 * API 時間以 UTC 傳輸，畫面統一顯示台北時間；金融金額不做前端轉型或運算。
 */
function formatTime(value: string | null) {
  return value ? timeFormatter.format(new Date(value)).replaceAll('-', '/') : '—';
}

/**
 * 接受啟動只表示背景作業已受理，成功與失敗仍以後續批次狀態為準。
 */
async function submitSettlement(batchId: string) {
  const result = await startSettlement(batchId);
  if (result) toast.success(`已受理 ${result.batchId} 結算，請查看進度。`);
}
</script>

<template>
  <div class="mx-auto flex min-h-full min-w-0 max-w-[1600px] flex-col gap-3 p-3 md:p-4">
    <!-- 操作與監控整合於 Vue；背景刷新保留目前選取、資料與捲動位置。 -->
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
        <h1 class="text-base font-semibold">借券利息結算</h1>
        <HelpHint label="結算與監控說明">
          <p>每 5 秒更新批次與所選明細。只有待處理批次可啟動，結算成功後不可重複執行。</p>
          <p class="mt-1">失敗批次請先查看錯誤日誌；連線中斷時保留最後資料，不代表結算失敗。</p>
        </HelpHint>
        <p class="text-[11px] text-muted-foreground">
          每 5 秒更新 · 最近更新：{{ formatTime(lastUpdatedTime) }}
        </p>
      </div>
      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="refreshing"
        @click="reloadMonitor()"
      >
        <RefreshCw
          class="size-3.5"
          :class="{ 'animate-spin': refreshing }"
          aria-hidden="true"
        />重新整理
      </Button>
    </div>

    <!-- 連線與啟動錯誤獨立顯示，不將尚未更新的快照改成業務失敗狀態。 -->
    <div
      v-if="listError"
      class="rounded-md border border-warning/30 bg-warning-muted px-3 py-2 text-xs"
      role="alert"
    >
      <p class="whitespace-pre-line text-warning">{{ listError }}</p>
      <p v-if="lastUpdatedTime" class="mt-1 text-muted-foreground">
        下方保留最後成功讀取的資料，狀態可能尚未更新。
      </p>
    </div>
    <div
      v-if="startError"
      class="rounded-md border border-warning/30 bg-warning-muted px-3 py-2 text-xs text-warning"
      role="alert"
    >
      {{ startError }}
    </div>
    <InterestBatchTable
      :batches="batches"
      :selected-batch-id="selectedBatchId"
      :starting-ids="startingIds"
      :unknown-ids="unknownIds"
      :loading="initialLoading"
      :failed="!!listError"
      @select="selectBatch"
      @start="submitSettlement"
    />

    <!-- 失敗批次以紅底工作區與錯誤日誌告警，保留中斷時的真實進度。 -->
    <section
      class="erp-panel overflow-hidden"
      :class="{ 'border-destructive/30 bg-danger-muted': selectedBatch?.processStatus === 9 }"
      aria-labelledby="interest-detail-title"
    >
      <div class="erp-panel-heading flex-wrap gap-y-1 py-2">
        <h2 id="interest-detail-title">
          批次明細<span v-if="selectedBatch" class="ml-2 font-normal text-muted-foreground">{{
            selectedBatch.batchId
          }}</span>
        </h2>
        <span
          v-if="refreshing && selectedBatchId"
          class="inline-flex items-center gap-1 text-[11px] font-normal text-muted-foreground"
          role="status"
          ><LoaderCircle class="size-3 animate-spin" aria-hidden="true" />更新中</span
        >
      </div>
      <div
        v-if="detailError"
        class="border-b bg-warning-muted px-3 py-2 text-xs text-warning"
        role="alert"
      >
        {{ detailError }}<span v-if="selectedDetail"> 下方保留前次讀取內容。</span>
      </div>
      <div
        v-if="selectedBatch"
        class="flex flex-wrap gap-x-5 gap-y-1 px-3 py-2 text-[11px] text-muted-foreground"
      >
        <p>開始時間（台北）：{{ formatTime(selectedBatch.startedTime) }}</p>
        <p>結束時間（台北）：{{ formatTime(selectedBatch.finishedTime) }}</p>
      </div>
      <div v-if="selectedBatch?.processStatus === 9" class="px-3 pb-3 text-xs" role="alert">
        <h3 class="font-semibold text-destructive">
          結算失敗 · 進度 {{ selectedBatch.progressRate }}%
        </h3>
        <p class="mt-1 text-muted-foreground">本批次未完成結算，請先確認錯誤原因。</p>
        <pre
          class="mt-2 max-h-56 overflow-auto whitespace-pre-wrap break-words rounded border border-destructive/20 bg-card/70 p-3 font-mono text-[11px] text-destructive"
          >{{ selectedBatch.errorMessage || '尚無錯誤日誌，請重新整理取得最新內容。' }}</pre>
      </div>
      <!-- 只有成功批次呈現已提交的金額與明細，處理中的空結果不顯示為零元成功。 -->
      <template
        v-else-if="selectedBatch?.processStatus === 2 && selectedDetail?.batch.processStatus === 2"
      >
        <div
          class="flex flex-wrap items-baseline gap-x-5 gap-y-1 border-y bg-success-muted px-3 py-2 text-xs"
        >
          <p>
            預計總額：<strong class="tabular-nums">{{ selectedBatch.expectedAmt }}</strong> 元
          </p>
          <p class="text-success">
            明細合計：<strong class="tabular-nums">{{
              selectedDetail.calculatedAmt ?? '—'
            }}</strong>
            元
          </p>
          <span class="text-[11px] text-muted-foreground"
            >共 {{ selectedDetail.details.length }} 筆</span
          >
        </div>
        <div class="max-h-80 overflow-auto">
          <table
            class="erp-table min-w-[420px] border-separate border-spacing-0"
            aria-label="已完成結算的合約利息明細"
          >
            <thead class="sticky top-0 z-10 bg-muted [&_th]:bg-muted">
              <tr>
                <th scope="col">明細編號</th>
                <th scope="col" class="text-left!">合約編號</th>
                <th scope="col">每日利息（元）</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="record in selectedDetail.details" :key="record.detailId">
                <td>{{ record.detailId }}</td>
                <td class="text-left!">{{ record.contractNo }}</td>
                <td>{{ record.dailyInterest }}</td>
              </tr>
              <tr v-if="!selectedDetail.details.length">
                <td colspan="3" class="h-20! text-center! text-muted-foreground">
                  此批次沒有結算明細。
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
      <p v-else class="px-3 py-6 text-center text-xs text-muted-foreground">
        {{
          !selectedBatch
            ? '選擇批次後查看明細。'
            : selectedBatch.processStatus === 1
              ? '結算處理中，完成後顯示明細與校驗總額。'
              : selectedBatch.processStatus === 0
                ? '批次尚未啟動，啟動後將自動更新進度。'
                : '正在取得結算結果，請稍候或重新整理。'
        }}
      </p>
    </section>
  </div>
</template>
