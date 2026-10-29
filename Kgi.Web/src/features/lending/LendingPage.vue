<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue';
import { ChevronUp, LoaderCircle, RefreshCw, RotateCcw, Upload } from '@lucide/vue';
import { CollapsibleContent, CollapsibleRoot, CollapsibleTrigger } from 'reka-ui';
import { toast } from 'vue-sonner';
import { Button } from '@/components/ui/button';
import ConfirmDialog from '@/components/ConfirmDialog.vue';
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
  resetting,
  resetError,
  selectedFile,
  fileError,
  importing,
  importError,
  importNotice,
  canImport,
  reloadMonitor,
  selectFiles,
  importFile,
  resetMarketPrices,
} = useLendingMonitor();
const uploadExpanded = ref(false);

// 確認只保存原始 File 參照；不解析 CSV，也不把同檔名的新選檔視為同一份確認。
const importDialogOpen = ref(false);
const importSnapshot = shallowRef<File | null>(null);
const resetDialogOpen = ref(false);
const resetTradeDate = ref<string | null>(null);
const canConfirmImport = computed(
  () =>
    !!importSnapshot.value &&
    selectedFile.value === importSnapshot.value &&
    canImport.value &&
    !loading.value &&
    !importing.value &&
    !resetting.value,
);
const canReset = computed(
  () => !loading.value && !importing.value && !resetting.value && !!monitor.value?.tradeDate,
);

// 狀態變動同步讓原確認失效，避免下一次畫面更新前仍可送出舊的確認。
watch(
  [selectedFile, monitor, loading, importing, resetting, canImport],
  () => {
    importDialogOpen.value = false;
    resetDialogOpen.value = false;
    // 保留離場中的摘要，下次開窗再取代，避免送出後彈窗內容突然縮空。
  },
  { flush: 'sync' },
);

/**
 * 鎖定這次選取的 File，先讓使用者確認上傳及同日替換的影響。
 */
function requestImport() {
  if (
    !selectedFile.value ||
    !canImport.value ||
    loading.value ||
    importing.value ||
    resetting.value
  ) {
    return;
  }
  importSnapshot.value = selectedFile.value;
  resetDialogOpen.value = false;
  importDialogOpen.value = true;
}

/**
 * 成功寫入後收合匯入區，將空間還給清單；刷新錯誤仍由工作區獨立呈現。
 */
async function submitImport() {
  // 共用對話框先關閉再通知；確認有效性依快照判斷，不依對話框是否仍開啟。
  if (!canConfirmImport.value) return;
  const result = await importFile();
  if (result) {
    uploadExpanded.value = false;
    toast.success(`已匯入 ${result.importedCount} 筆行情`);
  }
}

/**
 * 只有目前有行情且沒有其他操作時，才顯示清除全部日期的確認。
 */
function requestReset() {
  if (!canReset.value) return;
  importDialogOpen.value = false;
  resetTradeDate.value = monitor.value?.tradeDate ?? null;
  resetDialogOpen.value = true;
}

/**
 * 確認後才清除行情；成功後收合匯入區，刷新失敗時保留獨立提示。
 */
async function submitReset() {
  if (!canReset.value) return;
  if (await resetMarketPrices()) {
    uploadExpanded.value = false;
    if (loadError.value) {
      toast.warning('行情已重置，但監控資料刷新失敗，請重試。');
    } else {
      toast.success('行情已重置，行情日已回到尚未匯入。');
    }
  }
}
</script>

<template>
  <CollapsibleRoot
    v-model:open="uploadExpanded"
    :unmount-on-hide="false"
    :disabled="importing || resetting"
    as="div"
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
          :disabled="loading || importing || resetting"
          @click="reloadMonitor()"
        >
          <RefreshCw class="size-3.5" :class="{ 'animate-spin': loading }" aria-hidden="true" />
          重新整理
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          :disabled="!canReset"
          @click="requestReset"
        >
          <LoaderCircle v-if="resetting" class="size-3.5 animate-spin" aria-hidden="true" />
          <RotateCcw v-else class="size-3.5" aria-hidden="true" />
          {{ resetting ? '重置中…' : '重置行情' }}
        </Button>
        <CollapsibleTrigger as-child>
          <Button
            type="button"
            :variant="uploadExpanded ? 'secondary' : 'outline'"
            size="sm"
            :disabled="importing || resetting"
          >
            <LoaderCircle v-if="importing" class="size-3.5 animate-spin" aria-hidden="true" />
            <ChevronUp v-else-if="uploadExpanded" class="size-3.5" aria-hidden="true" />
            <Upload v-else class="size-3.5" aria-hidden="true" />
            {{ importing ? '匯入中…' : uploadExpanded ? '收合匯入' : '匯入行情' }}
          </Button>
        </CollapsibleTrigger>
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

    <!-- 以內容實際高度收合，子元件持續掛載，選檔與預覽狀態可接續操作。 -->
    <CollapsibleContent class="erp-collapsible-content shrink-0">
      <MarketPriceUpload
        :file="selectedFile"
        :file-error="fileError"
        :import-error="importError"
        :notice="importNotice"
        :importing="importing || resetting"
        :can-import="canImport && !loading"
        @select="selectFiles"
        @import="requestImport"
      />
    </CollapsibleContent>

    <!-- 重置失敗不清空畫面，避免把未確認的結果顯示成尚未匯入。 -->
    <p
      v-if="resetError"
      class="shrink-0 rounded-md border border-destructive/30 bg-danger-muted px-3 py-2 text-xs text-destructive"
      role="alert"
    >
      {{ resetError }}
    </p>

    <!-- 刷新失敗保留舊快照；匯入或重置已成功時仍能清楚看見此提示。 -->
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
        :disabled="loading || importing || resetting"
        @click="reloadMonitor()"
        >重試</Button
      >
    </div>
    <LendingContractTable
      v-model:abnormal-only="abnormalOnly"
      class="lending-contracts"
      :contracts="monitor?.contracts ?? []"
      :loading="loading || resetting"
      :failed="!!loadError"
    />

    <!-- 寫入與清除都先確認影響，取消時只關閉對話框，不呼叫 API。 -->
    <ConfirmDialog
      v-model:open="importDialogOpen"
      title="確定匯入行情？"
      description="確認後將上傳並驗證整份 CSV。相同交易日已匯入的行情將整批替換，其他日期行情會保留。"
      confirm-text="確認匯入"
      :disabled="!canConfirmImport"
      @confirm="submitImport"
    >
      <p class="break-all">檔案：{{ importSnapshot?.name }}</p>
      <p class="mt-1">交易日期與全部資料將由後端驗證；驗證失敗不會寫入部分行情。</p>
    </ConfirmDialog>
    <ConfirmDialog
      v-model:open="resetDialogOpen"
      title="確定重置行情？"
      description="將清除所有日期已匯入的行情，行情日回到「尚未匯入」，全部合約恢復待補價。借券合約與權證資料會保留；行情需重新匯入 CSV 才能恢復。"
      confirm-text="確定重置"
      :disabled="!canReset"
      destructive
      @confirm="submitReset"
    >
      <p>目前畫面行情日：{{ resetTradeDate }}</p>
      <p class="mt-1">清除範圍包含全部日期，無法透過此畫面復原。</p>
    </ConfirmDialog>
  </CollapsibleRoot>
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
