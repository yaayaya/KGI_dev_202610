<script setup lang="ts">
import { ref } from 'vue';
import { ChevronUp, LoaderCircle, RefreshCw, Upload } from '@lucide/vue';
import { toast } from 'vue-sonner';
import { Button } from '@/components/ui/button';
import HelpHint from '@/components/HelpHint.vue';
import LendingContractTable from './LendingContractTable.vue';
import MarketPriceUpload from './MarketPriceUpload.vue';
import { useLendingMonitor } from './useLendingMonitor';

/**
 * Q2 以合約清單為主要工作區，統計採單列摘要，匯入操作需要時才展開。
 */
const {
  monitor,
  loading,
  loadError,
  abnormalOnly,
  selectedFile,
  fileError,
  importing,
  importError,
  importNotice,
  canImport,
  reloadMonitor,
  selectFiles,
  importFile,
} = useLendingMonitor();
const uploadExpanded = ref(false);

/**
 * 成功寫入後收合匯入區，將空間還給清單；刷新錯誤仍由工作區獨立呈現。
 */
async function submitImport() {
  const result = await importFile();
  if (result) {
    uploadExpanded.value = false;
    toast.success(`已匯入 ${result.importedCount} 筆行情`);
  }
}
</script>

<template>
  <div
    class="lending-workspace mx-auto flex h-full min-h-0 min-w-0 max-w-[1600px] flex-col gap-2.5 overflow-hidden p-3 md:p-4"
  >
    <!-- 標題只保留行情日期與必要操作，匯入預設收合，讓表格取得更多垂直空間。 -->
    <div class="flex shrink-0 flex-wrap items-center justify-between gap-2">
      <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
        <h1 class="text-base font-semibold">借券維持率監控</h1>
        <p class="text-xs text-muted-foreground">
          行情日：<span class="font-medium text-foreground">{{
            monitor ? (monitor.tradeDate ?? '尚未匯入') : '—'
          }}</span>
        </p>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          :disabled="loading || importing"
          @click="reloadMonitor()"
        >
          <RefreshCw class="size-3.5" :class="{ 'animate-spin': loading }" aria-hidden="true" />
          重新整理
        </Button>
        <Button
          type="button"
          :variant="uploadExpanded ? 'secondary' : 'outline'"
          size="sm"
          :disabled="importing"
          :aria-expanded="uploadExpanded"
          aria-controls="lending-upload-panel"
          @click="uploadExpanded = !uploadExpanded"
        >
          <LoaderCircle v-if="importing" class="size-3.5 animate-spin" aria-hidden="true" />
          <ChevronUp v-else-if="uploadExpanded" class="size-3.5" aria-hidden="true" />
          <Upload v-else class="size-3.5" aria-hidden="true" />
          {{ importing ? '匯入中…' : uploadExpanded ? '收合匯入' : '匯入行情' }}
        </Button>
      </div>
    </div>

    <!-- 全體件數濃縮為單列摘要；下方搜尋與狀態條件只影響明細。 -->
    <section
      class="erp-panel flex shrink-0 flex-wrap items-center gap-x-5 gap-y-2 px-3 py-2 text-xs"
      aria-label="全體合約狀態件數"
    >
      <p class="inline-flex items-baseline gap-1.5">
        <span class="text-muted-foreground">全體合約</span>
        <strong class="font-semibold tabular-nums">{{
          monitor ? monitor.totalCount.toLocaleString('zh-TW') : '—'
        }}</strong>
        <span class="text-muted-foreground">件</span>
      </p>
      <p class="inline-flex items-baseline gap-1.5">
        <span class="text-success">正常</span>
        <strong class="font-semibold tabular-nums">{{
          monitor ? monitor.normalCount.toLocaleString('zh-TW') : '—'
        }}</strong>
      </p>
      <p class="inline-flex items-baseline gap-1.5">
        <span class="text-warning">預警</span>
        <strong class="font-semibold tabular-nums">{{
          monitor ? monitor.warningCount.toLocaleString('zh-TW') : '—'
        }}</strong>
      </p>
      <p class="inline-flex items-baseline gap-1.5">
        <span class="text-destructive">處置</span>
        <strong class="font-semibold tabular-nums">{{
          monitor ? monitor.callCount.toLocaleString('zh-TW') : '—'
        }}</strong>
      </p>
      <p
        v-if="monitor?.unpricedCount"
        class="inline-flex items-center gap-1.5 text-muted-foreground"
      >
        待補價
        <strong class="font-semibold tabular-nums">{{
          monitor.unpricedCount.toLocaleString('zh-TW')
        }}</strong>
        <HelpHint label="待補價合約說明">
          尚無當日行情的合約不列入正常、預警或處置件數；匯入包含全部合約標的的行情後才可計算。
        </HelpHint>
      </p>
    </section>

    <!-- 收合只隱藏匯入區，不卸載選檔與預覽狀態，展開後可接續操作。 -->
    <div v-show="uploadExpanded" id="lending-upload-panel" class="shrink-0">
      <MarketPriceUpload
        :file="selectedFile"
        :file-error="fileError"
        :import-error="importError"
        :notice="importNotice"
        :importing="importing"
        :can-import="canImport"
        @select="selectFiles"
        @import="submitImport"
      />
    </div>

    <!-- 刷新失敗保留舊快照；匯入已成功時仍能清楚看見此提示。 -->
    <div
      v-if="loadError"
      class="flex shrink-0 flex-wrap items-center gap-2 rounded-md border border-destructive/30 bg-danger-muted px-3 py-2 text-xs"
      role="alert"
    >
      <div class="min-w-0 flex-1">
        <p class="whitespace-pre-line text-destructive">{{ loadError }}</p>
        <p v-if="monitor" class="mt-0.5 text-muted-foreground">下方仍為前次載入的結果。</p>
      </div>
      <Button
        type="button"
        variant="outline"
        size="xs"
        :disabled="loading || importing"
        @click="reloadMonitor()"
        >重試</Button
      >
    </div>
    <LendingContractTable
      v-model:abnormal-only="abnormalOnly"
      class="lending-contracts"
      :contracts="monitor?.contracts ?? []"
      :loading="loading"
      :failed="!!loadError"
    />
  </div>
</template>

<style scoped>
/* 一般高度只捲動表身；短視窗展開匯入時仍可捲動工作區，不裁切條件與分頁。 */
@media (max-height: 620px) {
  .lending-workspace {
    overflow-y: auto;
  }

  .lending-contracts {
    min-height: 20rem;
    flex-shrink: 0;
  }
}
</style>
