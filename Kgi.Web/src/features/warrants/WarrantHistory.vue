<script setup lang="ts">
import { History, LoaderCircle, RefreshCw } from '@lucide/vue';
import { Button } from '@/components/ui/button';
import type { TrialResponse } from './warrantTypes';

/**
 * 歷史明細顯示元件：接收父頁取得的最近十筆紀錄，不自行呼叫 API 或重新排序。
 */
defineProps<{
  records: TrialResponse[];
  loading: boolean;
  error: string;
  selected: boolean;
}>();
const emit = defineEmits<{ retry: [] }>();

// API 時間為 UTC，顯示時固定轉為台北時區，不受使用者電腦時區影響。
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
 * 僅格式化存檔時間；股價、理論價值與避險張數直接顯示 API 的精確字串。
 */
function formatTime(value: string | null) {
  return value ? timeFormatter.format(new Date(value)).replaceAll('-', '/') : '—';
}
</script>

<template>
  <section
    class="erp-panel flex min-h-0 flex-col overflow-hidden"
    aria-labelledby="trial-history-title"
    :aria-busy="loading"
  >
    <!-- 標題與重新整理：只通知父頁重取目前選券的紀錄。 -->
    <div class="erp-panel-heading">
      <h2 id="trial-history-title" class="flex items-center gap-1.5">
        <History class="size-3.5 text-muted-foreground" aria-hidden="true" />歷史明細<span
          class="text-[11px] font-normal text-muted-foreground"
          >最近 10 筆</span
        >
      </h2>
      <Button
        type="button"
        variant="ghost"
        size="icon-xs"
        :disabled="!selected || loading"
        aria-label="重新整理歷史紀錄"
        @click="emit('retry')"
        ><RefreshCw :class="['size-3.5', { 'animate-spin': loading }]" aria-hidden="true"
      /></Button>
    </div>

    <!-- 讀取失敗時保留既有紀錄，另外顯示錯誤與重試入口。 -->
    <div
      v-if="error"
      class="flex flex-wrap items-center gap-2 border-b bg-destructive/5 px-3 py-2 text-xs"
      role="alert"
    >
      <p class="flex-1 text-destructive">{{ error }}</p>
      <Button type="button" variant="outline" size="xs" @click="emit('retry')">重試</Button>
    </div>

    <!-- 表格區獨立捲動；窄螢幕保留數值欄寬，避免為塞入畫面而截斷金額。 -->
    <div class="min-h-0 flex-1 overflow-auto">
      <table class="erp-table min-w-[460px]" aria-label="該權證最近十筆試算紀錄">
        <!-- 固定表頭保留儲存時間與各數值的單位。 -->
        <thead class="sticky top-0 z-10">
          <tr>
            <th scope="col" class="text-left!">儲存時間</th>
            <th scope="col">標的股價（元）</th>
            <th scope="col">理論價值（元）</th>
            <th scope="col">避險張數（張）</th>
          </tr>
        </thead>
        <tbody>
          <!-- 紀錄順序沿用後端結果，金融欄位不轉成 JavaScript number。 -->
          <tr
            v-for="record in records"
            :key="record.logId ?? `${record.createdTime}-${record.marketPrice}`"
          >
            <td class="text-left! text-muted-foreground">{{ formatTime(record.createdTime) }}</td>
            <td>{{ record.marketPrice }}</td>
            <td>{{ record.theoryPrice }}</td>
            <td>{{ record.hedgeQty }}</td>
          </tr>

          <!-- 無資料時區分載入、未選券、無紀錄及失敗，避免混成同一種空狀態。 -->
          <tr v-if="records.length === 0">
            <td colspan="4" class="h-24! text-center! text-muted-foreground">
              <span v-if="loading" class="inline-flex items-center gap-2" role="status"
                ><LoaderCircle class="size-3.5 animate-spin" aria-hidden="true" />載入紀錄中</span
              >
              <span v-else-if="!selected">選擇權證後顯示歷史紀錄</span>
              <span v-else-if="!error">尚無儲存紀錄</span>
              <span v-else>紀錄暫時無法顯示</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
