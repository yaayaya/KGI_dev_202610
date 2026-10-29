<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue';
import { Building2, LoaderCircle, Plus, RefreshCw } from '@lucide/vue';
import { toast } from 'vue-sonner';
import type { StockTransferRequest } from '@/api/generated/kgiApi.schemas';
import { Button } from '@/components/ui/button';
import HelpHint from '@/components/HelpHint.vue';
import ShareholderRegister from './ShareholderRegister.vue';
import TransferEditor from './TransferEditor.vue';
import MeetingPanel from './MeetingPanel.vue';
import DividendPanel from './DividendPanel.vue';
import TransferHistory from './TransferHistory.vue';
import StockDialog from './StockDialog.vue';
import { formatExact } from './stockAffairsApi';
import { useStockAffairsWorkspace } from './useStockAffairsWorkspace';

/**
 * 固定高度的股務工作區；表格內捲動，輸入與確認透過 Dialog，結果提示不占版面。
 */
const {
  companies,
  companyCode,
  company,
  register,
  transfers,
  meetings,
  dividends,
  loading,
  loadError,
  notices,
  dividendPreview,
  detailLoading,
  detailError,
  detailKind,
  writing,
  actionError,
  actionNotice,
  reload,
  applyTransfers,
  sendNotices,
  confirmDividend,
  previewNotices,
  previewDividend,
} = useStockAffairsWorkspace();
const tabs = [
  { id: 'register', label: '股東名簿' },
  { id: 'meetings', label: '股東會管理' },
  { id: 'dividends', label: '股利發放' },
  { id: 'history', label: '異動紀錄' },
] as const;
const currentTab = ref<(typeof tabs)[number]['id']>('register');
const starter = ref<{ shareholderId: string; version: number } | null>(null);
const completedVersion = ref(0);
const transferOpen = ref(false);
const confirmationOpen = ref(false);
type Confirmation = {
  kind: 'notice' | 'dividend';
  id: string;
  companyCode: string;
  rows: { label: string; value: string }[];
};
const confirmation = ref<Confirmation | null>(null);
const confirmationTitle = computed(() =>
  confirmation.value?.kind === 'notice' ? '確認寄發通知' : '確認股利發放',
);
const canConfirm = computed(() => {
  const target = confirmation.value;
  if (!target || target.companyCode !== companyCode.value || loading.value || detailLoading.value)
    return false;
  return target.kind === 'notice'
    ? notices.value?.meetingId === target.id &&
        !notices.value.noticeSent &&
        notices.value.recipients.length > 0
    : dividendPreview.value?.batchId === target.id && dividendPreview.value.canConfirm;
});
const loadToastId = 'stock-affairs-load-error';

/**
 * 寫入與刷新結果分開提示，Toast 固定於視窗，不擠壓表格。
 */
function showResult() {
  if (loadError.value) {
    toast.warning(actionNotice.value, {
      action: {
        label: '重新整理',
        onClick: () => {
          void reload();
        },
      },
      duration: 8000,
    });
  } else toast.success(actionNotice.value);
}

/**
 * 從工具列或名簿開啟股權異動，不在工作區插入表單。
 */
function openTransfer(shareholderId?: string) {
  if (writing.value || loading.value || !register.value) return;
  actionError.value = '';
  if (shareholderId) starter.value = { shareholderId, version: (starter.value?.version ?? 0) + 1 };
  transferOpen.value = true;
}

/**
 * 只有成功寫入才關閉並清空表單，驗證錯誤保留在彈窗。
 */
async function submitTransfers(request: StockTransferRequest) {
  if (await applyTransfers(request)) {
    completedVersion.value++;
    showResult();
  }
}

/**
 * 寄發前先確認公司、會議及影響股東人數，尚不發出寫入請求。
 */
function requestNotices(id: string) {
  const detail = notices.value;
  const meeting = meetings.value.find((row) => row.meetingId === id);
  if (!detail || detail.meetingId !== id || detail.noticeSent || !meeting || writing.value) return;
  actionError.value = '';
  confirmation.value = {
    kind: 'notice',
    id,
    companyCode: companyCode.value,
    rows: [
      { label: '公司', value: `${companyCode.value} ${company.value?.companyName ?? ''}` },
      { label: '會議', value: id },
      { label: '基準日', value: meeting.recordDate },
      { label: '通知股東', value: `${detail.recipients.length.toLocaleString('zh-TW')} 人` },
    ],
  };
  confirmationOpen.value = true;
}

/**
 * 股利確認列出已對帳的批次、精確總額與單位，再交由後端重算。
 */
function requestDividend(id: string) {
  const preview = dividendPreview.value;
  const batch = dividends.value.find((row) => row.batchId === id);
  if (!preview || preview.batchId !== id || !preview.canConfirm || !batch || writing.value) return;
  actionError.value = '';
  confirmation.value = {
    kind: 'dividend',
    id,
    companyCode: companyCode.value,
    rows: [
      { label: '公司', value: `${companyCode.value} ${company.value?.companyName ?? ''}` },
      { label: '批次', value: id },
      { label: '基準日', value: batch.recordDate },
      { label: '發放股東', value: `${preview.details.length.toLocaleString('zh-TW')} 人` },
      {
        label: preview.unit === 'SHARE' ? '配發股數' : '發放總額',
        value: `${formatExact(preview.calculatedTotal)} ${preview.unit === 'SHARE' ? '股' : '元'}`,
      },
    ],
  };
  confirmationOpen.value = true;
}

/**
 * 保持確認目標與目前公司及預覽一致，失敗留在 Dialog，成功只提示一次。
 */
async function confirmOperation() {
  const target = confirmation.value;
  if (!target || !canConfirm.value || writing.value) return;
  if (target.kind === 'notice') await sendNotices(target.id);
  else await confirmDividend(target.id);
  if (target.companyCode !== companyCode.value || actionError.value || !actionNotice.value) return;
  confirmationOpen.value = false;
  showResult();
}

/**
 * 載入失敗提供浮動重試；寫入後刷新失敗合併至該次結果，避免重複提示。
 */
watch(loadError, (error) => {
  if (!error) toast.dismiss(loadToastId);
  else if (!writing.value) {
    toast.error('股務資料讀取失敗', {
      id: loadToastId,
      description: error,
      duration: Infinity,
      action: {
        label: '重試',
        onClick: () => {
          void reload();
        },
      },
    });
  }
});
watch(companyCode, () => {
  transferOpen.value = false;
  confirmationOpen.value = false;
  starter.value = null;
});
onScopeDispose(() => toast.dismiss(loadToastId));
</script>

<template>
  <div
    class="mx-auto flex h-full min-h-0 w-full max-w-[1700px] flex-col gap-2 overflow-hidden p-3 md:p-4"
  >
    <!-- 固定工具列與統計，表身承接剩餘高度。 -->
    <div class="flex shrink-0 flex-wrap items-center justify-between gap-2">
      <div class="flex min-w-0 flex-wrap items-center gap-2.5">
        <h1 class="flex items-center gap-1.5 text-base font-semibold">
          <Building2 class="size-4 text-primary" aria-hidden="true" />股務代理作業
        </h1>
        <label class="flex items-center gap-2 text-xs"
          ><span class="text-muted-foreground">公司</span>
          <select
            v-model="companyCode"
            class="erp-input w-52!"
            :disabled="writing || !companies.length"
            aria-label="選擇公司"
          >
            <option v-if="!companies.length" value="">
              {{ loading ? '載入中…' : '尚無公司' }}
            </option>
            <option v-for="row in companies" :key="row.companyCode" :value="row.companyCode">
              {{ row.companyCode }} {{ row.companyName }}
            </option>
          </select>
        </label>
        <span class="rounded bg-secondary px-2 py-1 text-[11px] text-secondary-foreground"
          >模擬作業</span
        >
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <Button
          type="button"
          size="sm"
          :disabled="writing || loading || !register"
          @click="openTransfer()"
          ><Plus class="size-3.5" aria-hidden="true" />股權異動</Button
        >
        <Button
          type="button"
          variant="outline"
          size="sm"
          :disabled="loading || writing"
          @click="reload"
          ><RefreshCw
            class="size-3.5"
            :class="{ 'animate-spin': loading }"
            aria-hidden="true"
          />重新整理</Button
        >
      </div>
    </div>
    <section
      class="erp-panel flex shrink-0 flex-wrap items-center gap-x-6 gap-y-1 px-3 py-2 text-xs"
      aria-label="公司股權統計"
    >
      <p class="inline-flex items-baseline gap-2">
        <span class="text-muted-foreground">股東人數</span
        ><strong class="tabular-nums">{{
          company ? company.shareholderCount.toLocaleString('zh-TW') : '—'
        }}</strong
        ><span class="text-muted-foreground">人</span>
      </p>
      <p class="inline-flex items-baseline gap-2">
        <span class="text-muted-foreground">總發行股數</span
        ><strong class="tabular-nums">{{ company ? formatExact(company.totalShares) : '—' }}</strong
        ><span class="text-muted-foreground">股</span>
      </p>
      <p class="inline-flex items-baseline gap-2">
        <span class="text-muted-foreground">已質押股數</span
        ><strong class="tabular-nums">{{
          company ? formatExact(company.pledgedShares) : '—'
        }}</strong
        ><span class="text-muted-foreground">股</span>
      </p>
      <p class="inline-flex items-center gap-1.5">
        <span class="text-muted-foreground">已質押比例</span
        ><strong class="text-warning tabular-nums">{{
          company ? `${company.pledgedRatio}%` : '—'
        }}</strong
        ><HelpHint label="公司統計範圍"
          >股東人數僅計算正持股者，已質押比例為質押股數除以總發行股數。</HelpHint
        >
      </p>
    </section>
    <p class="sr-only" role="status">
      {{ writing ? '正在提交，請等候完成。' : loading ? '正在讀取資料。' : '' }}
    </p>

    <!-- 工作區在切換動畫期間共用相同高度，避免疊出頁面捲軸。 -->
    <nav class="flex shrink-0 gap-1 overflow-x-auto border-b" aria-label="股務工作區">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        type="button"
        class="shrink-0 border-b-2 px-4 py-2 text-xs font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60"
        :class="
          currentTab === tab.id
            ? 'border-primary text-primary'
            : 'border-transparent text-muted-foreground hover:text-foreground'
        "
        :aria-current="currentTab === tab.id ? 'page' : undefined"
        :disabled="writing"
        @click="currentTab = tab.id"
      >
        {{ tab.label }}
      </button>
    </nav>
    <div class="relative min-h-0 flex-1 overflow-hidden">
      <Transition name="stock-panel"
        ><div v-show="currentTab === 'register'" class="stock-pane">
          <ShareholderRegister
            :key="`${companyCode}-register`"
            :shareholders="register?.shareholders ?? []"
            :loading="loading"
            :disabled="writing || loading"
            @operate="openTransfer"
          /></div
      ></Transition>
      <Transition name="stock-panel"
        ><MeetingPanel
          v-show="currentTab === 'meetings'"
          :key="`${companyCode}-meetings`"
          class="stock-pane"
          :meetings="meetings"
          :notices="notices"
          :loading="loading"
          :detail-loading="detailKind === 'notice' && detailLoading"
          :detail-error="detailKind === 'notice' ? detailError : ''"
          :busy="writing"
          @preview="previewNotices"
          @send="requestNotices"
      /></Transition>
      <Transition name="stock-panel"
        ><DividendPanel
          v-show="currentTab === 'dividends'"
          :key="`${companyCode}-dividends`"
          class="stock-pane"
          :dividends="dividends"
          :preview="dividendPreview"
          :loading="loading"
          :detail-loading="detailKind === 'dividend' && detailLoading"
          :detail-error="detailKind === 'dividend' ? detailError : ''"
          :busy="writing"
          @preview="previewDividend"
          @confirm="requestDividend"
      /></Transition>
      <Transition name="stock-panel"
        ><TransferHistory
          v-show="currentTab === 'history'"
          :key="`${companyCode}-history`"
          class="stock-pane"
          :records="transfers"
          :loading="loading"
      /></Transition>
    </div>

    <!-- 輸入及最終確認皆移至浮層，不改變工作區高度。 -->
    <TransferEditor
      :key="`${companyCode}-editor`"
      v-model:open="transferOpen"
      :candidates="register?.candidates ?? []"
      :ready="!!register && !loading"
      :busy="writing"
      :starter="starter"
      :completed-version="completedVersion"
      :error="actionError"
      @clear-error="actionError = ''"
      @submit="submitTransfers"
    />
    <StockDialog
      v-model:open="confirmationOpen"
      :title="confirmationTitle"
      :busy="writing"
      :description="
        confirmation?.kind === 'notice'
          ? '確認本次模擬寄發通知。'
          : '確認後將保存配發明細並鎖定批次，此為模擬作業。'
      "
    >
      <dl class="space-y-3 text-sm">
        <div
          v-for="row in confirmation?.rows ?? []"
          :key="row.label"
          class="grid grid-cols-[80px_minmax(0,1fr)] gap-3"
        >
          <dt class="text-muted-foreground">{{ row.label }}</dt>
          <dd class="break-words font-medium tabular-nums">{{ row.value }}</dd>
        </div>
      </dl>
      <p
        v-if="actionError"
        role="alert"
        class="mt-4 max-h-24 overflow-y-auto rounded-md bg-danger-muted p-3 text-xs whitespace-pre-line text-destructive"
      >
        {{ actionError }}
      </p>
      <template #footer>
        <Button
          type="button"
          variant="outline"
          :disabled="writing"
          @click="confirmationOpen = false"
          >取消</Button
        >
        <Button type="button" :disabled="writing || !canConfirm" @click="confirmOperation"
          ><LoaderCircle v-if="writing" class="size-3.5 animate-spin" aria-hidden="true" />{{
            writing ? '處理中…' : confirmation?.kind === 'notice' ? '確認寄發' : '確認發放'
          }}</Button
        >
      </template>
    </StockDialog>
  </div>
</template>

<style scoped>
/* 工作區僅在既有高度內轉場，隱藏頁籤的狀態仍保留。 */
.stock-pane {
  position: absolute;
  inset: 0;
  min-height: 0;
}
.stock-panel-enter-active {
  transition:
    opacity 160ms ease-out,
    transform 160ms ease-out;
}
.stock-panel-leave-active {
  transition: opacity 100ms ease-in;
}
.stock-panel-enter-from {
  opacity: 0;
  transform: translateY(4px);
}
.stock-panel-leave-to {
  opacity: 0;
}
@media (prefers-reduced-motion: reduce) {
  .stock-panel-enter-active,
  .stock-panel-leave-active {
    transition: none;
  }
}
</style>
