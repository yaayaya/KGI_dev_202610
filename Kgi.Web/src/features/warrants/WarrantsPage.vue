<script setup lang="ts">
import { ArrowLeft, LoaderCircle, Save } from '@lucide/vue';
import { useMediaQuery } from '@vueuse/core';
import { nextTick, ref, watch } from 'vue';
import { toast } from 'vue-sonner';
import { Button } from '@/components/ui/button';
import ConfirmDialog from '@/components/ConfirmDialog.vue';
import HelpHint from '@/components/HelpHint.vue';
import { useWarrantWorkspace } from './useWarrantWorkspace';
import WarrantHistory from './WarrantHistory.vue';
import WarrantList from './WarrantList.vue';
import type { WarrantResponse } from './warrantTypes';

/**
 * Q1 畫面組合入口：負責清單、基本資料、試算與歷史區的呈現，以及操作後的焦點。
 * API 請求、股價驗證及存檔狀態集中在 useWarrantWorkspace，這裡不計算財務公式。
 */
// 工作區資料與動作：子元件只接收畫面所需狀態，透過事件要求查詢或選券。
const {
  warrants,
  loadingList,
  listError,
  selectedWarrant,
  marketPrice,
  preview,
  previewLoading,
  previewError,
  validationMessage,
  history,
  historyLoading,
  historyError,
  saving,
  saveError,
  canSave,
  selectWarrant,
  reloadList,
  reloadHistory,
  retryPreview,
  saveTrial,
} = useWarrantWorkspace();

// 畫面專用狀態：手機的清單／明細切換，以及切換後要接回焦點的控制項。
const isDesktop = useMediaQuery('(min-width: 768px)');
const showMobileDetail = ref(false);
const priceInput = ref<HTMLInputElement>();
const detailHeading = ref<HTMLElement>();
const warrantList = ref<InstanceType<typeof WarrantList>>();
const saveConfirmationOpen = ref(false);
const saveSnapshot = ref<{
  warrantId: string;
  marketPrice: string;
  theoryPrice: string;
  hedgeQty: string;
} | null>(null);
// 庫存是整數，可加千分位；價格與比例則直接顯示 API 的十進位字串。
const qtyFormatter = new Intl.NumberFormat('zh-TW');

// 確認期間內容若改變，關閉舊確認，避免顯示前一份試算卻儲存目前輸入。
watch(
  [() => selectedWarrant.value?.warrantId, marketPrice, preview],
  () => {
    if (!saveConfirmationOpen.value) return;
    saveConfirmationOpen.value = false;
    saveSnapshot.value = null;
  },
  { flush: 'sync' },
);

/**
 * 選券後開啟明細；桌面聚焦股價欄，手機聚焦標題，避免立即彈出鍵盤遮住資料。
 */
async function select(warrant: WarrantResponse) {
  if (saving.value) return;
  selectWarrant(warrant);
  showMobileDetail.value = true;
  await nextTick();
  if (isDesktop.value) priceInput.value?.focus({ preventScroll: true });
  else detailHeading.value?.focus({ preventScroll: true });
}

/**
 * 手機返回清單時還原原選券的焦點，避免明細隱藏後鍵盤焦點遺失。
 */
async function backToList() {
  showMobileDetail.value = false;
  await nextTick();
  warrantList.value?.focusSelection();
}

/**
 * 表單及儲存按鈕共用確認入口，Enter 不會略過確認直接呼叫 API。
 */
function requestSave() {
  if (saveConfirmationOpen.value || !canSave.value || !selectedWarrant.value || !preview.value)
    return;
  saveSnapshot.value = {
    warrantId: selectedWarrant.value.warrantId,
    marketPrice: marketPrice.value.trim(),
    theoryPrice: preview.value.theoryPrice,
    hedgeQty: preview.value.hedgeQty,
  };
  saveConfirmationOpen.value = true;
}

/**
 * 使用者確認後再比對當前選券、價格及預覽，確保只儲存剛才確認的內容。
 */
async function confirmSave() {
  const snapshot = saveSnapshot.value;
  // 摘要保留到下次開窗，讓離場動畫期間仍顯示使用者剛確認的內容。
  if (
    !snapshot ||
    !canSave.value ||
    snapshot.warrantId !== selectedWarrant.value?.warrantId ||
    snapshot.marketPrice !== marketPrice.value.trim() ||
    snapshot.theoryPrice !== preview.value?.theoryPrice ||
    snapshot.hedgeQty !== preview.value?.hedgeQty
  ) {
    toast.error('試算內容已變更，請重新確認後儲存。');
    return;
  }
  await save();
}

/**
 * 僅在存檔成功後提示並準備下一筆輸入；清空表單與刷新歷史由工作區負責。
 */
async function save() {
  const code = selectedWarrant.value?.warrantId;
  if (!(await saveTrial())) return;
  toast.success(`${code} 試算結果已儲存`);
  await nextTick();
  // 非同步完成時若已換券、輸入新資料或開啟對話框，就不奪走目前操作的焦點。
  if (
    code !== selectedWarrant.value?.warrantId ||
    marketPrice.value ||
    document.activeElement?.closest('[role="dialog"], [role="alertdialog"]')
  )
    return;
  if (isDesktop.value) priceInput.value?.focus({ preventScroll: true });
  else detailHeading.value?.focus({ preventScroll: true });
}
</script>

<template>
  <!-- Q1 工作頁：共用 Header 與系統選單由 AppLayout 提供，這裡只組合功能內容。 -->
  <div class="flex h-full min-h-0 flex-col px-3 pb-3 sm:px-4" data-testid="warrants-page">
    <div class="flex min-h-10 shrink-0 items-center">
      <h1 class="text-base font-semibold tracking-wide">權證試算</h1>
    </div>
    <div
      class="grid min-h-0 flex-1 gap-3 md:grid-cols-[204px_minmax(0,1fr)] xl:grid-cols-[224px_minmax(0,1fr)]"
    >
      <!-- 左側導覽：搜尋與 CALL／PUT 篩選留在清單內，選券事件交回本頁處理。 -->
      <WarrantList
        ref="warrantList"
        :class="showMobileDetail ? 'hidden md:flex' : 'flex'"
        :warrants="warrants"
        :selected-id="selectedWarrant?.warrantId"
        :loading="loadingList"
        :error="listError"
        :disabled="saving"
        @select="select"
        @retry="reloadList"
      />
      <!-- 右側明細：桌面與清單並排；手機切換顯示，避免兩區同時擠在窄畫面。 -->
      <div
        :class="[
          'min-h-0 flex-col gap-3 overflow-y-auto md:flex',
          showMobileDetail ? 'flex' : 'hidden',
        ]"
      >
        <Button
          type="button"
          variant="outline"
          size="sm"
          class="w-fit shrink-0 md:hidden"
          @click="backToList"
          ><ArrowLeft aria-hidden="true" />返回權證清單</Button
        >
        <template v-if="selectedWarrant">
          <!-- 基本資料區：呈現目前選券的識別與計算條件，與下方輸入股價清楚區分。 -->
          <section
            class="erp-panel shrink-0 border-l-[3px] border-l-primary"
            aria-label="權證基本資料"
          >
            <div class="flex flex-wrap items-center gap-x-6 gap-y-3 p-3">
              <div class="min-w-[160px]">
                <p class="mb-1 text-[11px] font-medium text-muted-foreground">權證代碼</p>
                <div class="flex flex-wrap items-center gap-2">
                  <h2
                    ref="detailHeading"
                    tabindex="-1"
                    class="text-xl font-semibold tracking-wide text-primary tabular-nums"
                  >
                    {{ selectedWarrant.warrantId }}
                  </h2>
                  <span
                    :class="[
                      'rounded-md border px-2 py-0.5 text-xs font-semibold',
                      selectedWarrant.warrantType === 'CALL'
                        ? 'border-primary/15 bg-accent text-accent-foreground'
                        : 'border-brand-orange/20 bg-secondary text-secondary-foreground',
                    ]"
                    >{{ selectedWarrant.warrantType === 'CALL' ? 'CALL 認購' : 'PUT 認售' }}</span
                  >
                </div>
              </div>
              <dl
                class="grid min-w-0 flex-1 basis-[360px] grid-cols-[repeat(auto-fit,minmax(100px,1fr))] gap-x-5 gap-y-2 tabular-nums [&_dd]:break-all"
              >
                <div>
                  <dt class="flex items-center gap-0.5 text-xs font-medium text-muted-foreground">
                    履約價（元）<HelpHint label="履約價說明"
                      >權證約定的執行價格，用於與標的股價比較。</HelpHint
                    >
                  </dt>
                  <dd class="mt-1 text-lg leading-6 font-semibold">
                    {{ selectedWarrant.strikePrice }}
                  </dd>
                </div>
                <div>
                  <dt class="flex items-center gap-0.5 text-xs font-medium text-muted-foreground">
                    行使比例<HelpHint label="行使比例說明"
                      >每單位權證對應的標的數量；0.1000 代表十換一。</HelpHint
                    >
                  </dt>
                  <dd class="mt-1 text-lg leading-6 font-semibold">
                    {{ selectedWarrant.conversionRatio }}
                  </dd>
                </div>
                <div>
                  <dt class="flex items-center gap-0.5 text-xs font-medium text-muted-foreground">
                    庫存（張）<HelpHint label="庫存說明"
                      >發行商目前持有的權證部位，單位為張。</HelpHint
                    >
                  </dt>
                  <dd class="mt-1 text-lg leading-6 font-semibold">
                    {{ qtyFormatter.format(selectedWarrant.positionQty) }}
                  </dd>
                </div>
              </dl>
            </div>
          </section>
          <!-- 試算區：送出股價、顯示預覽及存檔入口；所有結果都取自 API。 -->
          <section
            class="erp-panel shrink-0"
            aria-labelledby="trial-title"
            :aria-busy="previewLoading || saving"
          >
            <form @submit.prevent="requestSave">
              <div class="erp-panel-heading">
                <div class="flex items-center gap-0.5">
                  <h2 id="trial-title">試算</h2>
                  <!-- 說明須與後端 Calculate 的簡化模型同步；這些文字不參與前端計算。 -->
                  <HelpHint label="試算方式說明">
                    <div class="space-y-2.5">
                      <div>
                        <p class="font-semibold">理論價值</p>
                        <p>CALL：Max(0, 股價 − 履約價) × 行使比例</p>
                        <p>PUT：Max(0, 履約價 − 股價) × 行使比例</p>
                      </div>
                      <div>
                        <p class="font-semibold">建議避險張數</p>
                        <p>庫存張數 × 行使比例 × Delta</p>
                      </div>
                      <div>
                        <p class="font-semibold">Delta（題目簡化模型）</p>
                        <p>價內 0.8、價平 0.5、價外 0.2。</p>
                        <p>
                          CALL 股價高於履約價、PUT
                          股價低於履約價時為價內；股價等於履約價時為價平，其餘為價外。
                        </p>
                      </div>
                      <p class="border-t pt-2 text-[11px] text-muted-foreground">
                        理論價值取 4 位小數、避險張數取 2 位小數，採四捨五入。
                      </p>
                    </div>
                  </HelpHint>
                </div>
                <div class="flex items-center gap-2">
                  <span
                    v-if="previewLoading"
                    class="inline-flex items-center gap-1 text-[11px] font-normal text-muted-foreground"
                    role="status"
                    ><LoaderCircle class="size-3 animate-spin" aria-hidden="true" />試算中</span
                  >
                  <Button
                    type="submit"
                    size="sm"
                    :disabled="!canSave"
                    :aria-label="saving ? '儲存中' : '儲存試算結果'"
                    data-testid="save-trial"
                    ><LoaderCircle v-if="saving" class="animate-spin" aria-hidden="true" /><Save
                      v-else
                      aria-hidden="true"
                    />{{ saving ? '儲存中' : '儲存' }}</Button
                  >
                </div>
              </div>
              <!-- 輸入與唯讀結果：防抖、驗證及取消舊預覽由 useWarrantWorkspace 管理。 -->
              <div class="grid gap-4 px-3 py-3 sm:grid-cols-[minmax(140px,1fr)_1fr_1fr]">
                <div>
                  <label for="market-price" class="mb-1.5 block text-[11px] text-muted-foreground"
                    >標的股價（元）</label
                  >
                  <input
                    id="market-price"
                    ref="priceInput"
                    v-model="marketPrice"
                    class="erp-input max-w-56 tabular-nums"
                    inputmode="decimal"
                    autocomplete="off"
                    placeholder="輸入股價"
                    :disabled="saving"
                    :aria-invalid="!!validationMessage"
                    :aria-describedby="validationMessage ? 'market-price-help' : undefined"
                  />
                  <p
                    v-if="validationMessage"
                    id="market-price-help"
                    class="mt-1 text-[11px] text-destructive"
                    role="alert"
                  >
                    {{ validationMessage }}
                  </p>
                </div>
                <div class="sm:border-l sm:pl-4">
                  <span class="mb-1.5 block text-[11px] text-muted-foreground">理論價值（元）</span
                  ><output
                    for="market-price"
                    class="block text-xl leading-tight font-medium break-all text-primary tabular-nums"
                    aria-label="理論價值"
                    >{{ preview?.theoryPrice ?? '—' }}</output
                  >
                </div>
                <div class="sm:border-l sm:pl-4">
                  <span class="mb-1.5 block text-[11px] text-muted-foreground"
                    >建議避險張數（張）</span
                  ><output
                    for="market-price"
                    class="block text-xl leading-tight font-medium break-all text-primary tabular-nums"
                    aria-label="建議避險張數"
                    >{{ preview?.hedgeQty ?? '—' }}</output
                  >
                </div>
              </div>
              <!-- 請求錯誤區：預覽提供重試，存檔失敗則保留原股價與結果供使用者確認。 -->
              <div
                v-if="previewError"
                class="flex flex-wrap items-center gap-2 border-t bg-destructive/5 px-3 py-2 text-xs"
                role="alert"
              >
                <p class="flex-1 text-destructive">{{ previewError }}</p>
                <Button type="button" variant="outline" size="xs" @click="retryPreview"
                  >重新試算</Button
                >
              </div>
              <p
                v-if="saveError"
                class="border-t bg-destructive/5 px-3 py-2 text-xs text-destructive"
                role="alert"
              >
                {{ saveError }}
              </p>
            </form>
          </section>
        </template>
        <section
          v-else
          class="flex min-h-40 flex-1 items-center justify-center p-6 text-center text-muted-foreground"
        >
          <!-- 初次未選券的空狀態，不自動代選權證或預填股價。 -->
          <div>
            <h2 class="text-sm">選擇一檔權證開始試算</h2>
          </div>
        </section>
        <!-- 歷史區：選券後才呈現；讀取狀態由工作區提供，表格與時間格式交給子元件。 -->
        <WarrantHistory
          v-if="selectedWarrant"
          class="min-h-32 max-h-[400px]"
          :records="history"
          :loading="historyLoading"
          :error="historyError"
          :selected="!!selectedWarrant"
          @retry="reloadHistory"
        />
      </div>
    </div>
    <!-- 儲存確認：摘要使用開窗時快照，取消時不送出任何寫入請求。 -->
    <ConfirmDialog
      v-model:open="saveConfirmationOpen"
      title="儲存試算結果"
      description="請確認權證與股價，確認後將新增一筆試算紀錄。"
      confirm-text="確認儲存"
      :disabled="!canSave"
      @confirm="confirmSave"
    >
      <dl v-if="saveSnapshot" class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm tabular-nums">
        <div>
          <dt class="text-xs text-muted-foreground">權證代碼</dt>
          <dd class="mt-1 font-semibold">{{ saveSnapshot.warrantId }}</dd>
        </div>
        <div>
          <dt class="text-xs text-muted-foreground">標的股價（元）</dt>
          <dd class="mt-1 font-semibold break-all">{{ saveSnapshot.marketPrice }}</dd>
        </div>
        <div>
          <dt class="text-xs text-muted-foreground">理論價值（元）</dt>
          <dd class="mt-1 font-semibold break-all">{{ saveSnapshot.theoryPrice }}</dd>
        </div>
        <div>
          <dt class="text-xs text-muted-foreground">建議避險張數（張）</dt>
          <dd class="mt-1 font-semibold break-all">{{ saveSnapshot.hedgeQty }}</dd>
        </div>
      </dl>
    </ConfirmDialog>
  </div>
</template>
