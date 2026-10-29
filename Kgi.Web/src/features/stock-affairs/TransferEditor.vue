<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { LoaderCircle, Plus, Trash2 } from '@lucide/vue';
import type { StockCandidateResponse, StockTransferRequest } from '@/api/generated/kgiApi.schemas';
import { Button } from '@/components/ui/button';
import StockDialog from './StockDialog.vue';

/**
 * 單筆與批次異動共用受控彈窗，先編輯再核對摘要；取消保留草稿與相同內容的請求編號。
 */
const props = defineProps<{
  candidates: StockCandidateResponse[];
  busy: boolean;
  ready: boolean;
  starter: { shareholderId: string; version: number } | null;
  completedVersion: number;
  error: string;
}>();
const open = defineModel<boolean>('open', { required: true });
const emit = defineEmits<{ submit: [request: StockTransferRequest]; 'clear-error': [] }>();
type DraftRow = {
  key: number;
  transferType: 'TRANSFER' | 'PLEDGE' | 'UNPLEDGE';
  fromShareholder: string;
  toShareholder: string;
  quantity: string;
};
let sequence = 0;
const newRow = (): DraftRow => ({
  key: ++sequence,
  transferType: 'TRANSFER',
  fromShareholder: '',
  toShareholder: '',
  quantity: '',
});
const rows = ref<DraftRow[]>([newRow()]);
const step = ref<'edit' | 'confirm'>('edit');
const localError = ref('');
const formElement = ref<HTMLFormElement | null>(null);
const confirmButton = ref<InstanceType<typeof Button> | null>(null);
const preparedRequest = ref<StockTransferRequest | null>(null);
const canSubmit = computed(() => props.ready && !props.busy && rows.value.length > 0);
const candidateNames = computed(
  () => new Map(props.candidates.map((candidate) => [candidate.shareholderId, candidate.name])),
);
const typeLabels = { TRANSFER: '股票過戶', PLEDGE: '質權設定', UNPLEDGE: '質權撤銷' };
let requestId = '';
let lastPayload = '';

// 開關只改顯示步驟，不清除草稿；同一彈窗避免巢狀焦點限制。
watch(open, (value) => {
  if (value) {
    step.value = 'edit';
    localError.value = '';
  }
});
watch(
  rows,
  () => {
    localError.value = '';
  },
  { deep: true },
);
watch(
  () => props.starter,
  async (starter) => {
    if (!starter || props.busy) return;
    const draft = { ...newRow(), fromShareholder: starter.shareholderId };
    if (rows.value.length === 1 && !rows.value[0]?.fromShareholder && !rows.value[0]?.quantity)
      rows.value = [draft];
    else if (rows.value.length < 100) rows.value.push(draft);
    else localError.value = '每批最多 100 筆，請先完成或刪除現有異動。';
    step.value = 'edit';
    open.value = true;
    await nextTick();
    focusRow(draft.key);
  },
);
watch(
  () => props.completedVersion,
  () => {
    rows.value = [newRow()];
    localError.value = '';
    preparedRequest.value = null;
    requestId = '';
    lastPayload = '';
    step.value = 'edit';
    open.value = false;
  },
);

/**
 * 新增、帶入或刪除後將焦點留在對應列，手機橫向捲動仍能看見目前欄位。
 */
function focusRow(key: number) {
  const input = formElement.value?.querySelector<HTMLSelectElement>(
    `[data-operation-row="${key}"] select`,
  );
  input?.focus();
  input?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
}
async function addRow() {
  if (rows.value.length >= 100 || props.busy) return;
  const row = newRow();
  rows.value.push(row);
  await nextTick();
  focusRow(row.key);
}
async function removeRow(index: number) {
  if (rows.value.length <= 1 || props.busy) return;
  rows.value.splice(index, 1);
  await nextTick();
  const row = rows.value[Math.min(index, rows.value.length - 1)];
  if (row) focusRow(row.key);
}

/**
 * 編輯完成後先建立確認摘要；股數及批次相依性仍由後端在交易內重新驗證。
 */
async function review() {
  if (!canSubmit.value || step.value !== 'edit') return;
  const items: StockTransferRequest['items'] = [];
  for (const [index, row] of rows.value.entries()) {
    const prefix = `第 ${index + 1} 筆：`;
    if (!row.fromShareholder) {
      localError.value = `${prefix}請選擇異動股東。`;
      return;
    }
    if (
      row.transferType === 'TRANSFER' &&
      (!row.toShareholder || row.fromShareholder === row.toShareholder)
    ) {
      localError.value = `${prefix}請選擇不同的受讓股東。`;
      return;
    }
    if (!row.quantity.trim()) {
      localError.value = `${prefix}請輸入股數。`;
      return;
    }
    const quantity = Number(row.quantity);
    if (
      !/^\d+$/.test(row.quantity) ||
      !Number.isSafeInteger(quantity) ||
      quantity <= 0 ||
      quantity > 2_147_483_647
    ) {
      localError.value = `${prefix}股數須為 1 至 2,147,483,647 的整數。`;
      return;
    }
    items.push({
      transferType: row.transferType,
      fromShareholder: row.fromShareholder,
      toShareholder: row.transferType === 'TRANSFER' ? row.toShareholder : null,
      quantity,
    });
  }
  const payload = JSON.stringify(items);
  if (payload !== lastPayload || !requestId) requestId = crypto.randomUUID();
  lastPayload = payload;
  preparedRequest.value = { requestId, items };
  localError.value = '';
  step.value = 'confirm';
  await nextTick();
  (confirmButton.value?.$el as HTMLButtonElement | undefined)?.focus();
}

/**
 * 摘要確認才提交，錯誤時保留相同請求；成功清理由 completedVersion 驅動。
 */
function submitConfirmed() {
  if (step.value !== 'confirm' || !canSubmit.value || !preparedRequest.value) return;
  emit('submit', preparedRequest.value);
}
async function returnToEdit() {
  if (props.busy) return;
  emit('clear-error');
  step.value = 'edit';
  await nextTick();
  const row = rows.value[0];
  if (row) focusRow(row.key);
}
</script>

<template>
  <StockDialog
    v-model:open="open"
    :title="step === 'confirm' ? '確認股權異動' : '股權異動'"
    :description="
      step === 'confirm'
        ? '請核對股東與股數，確認後送出整批異動。'
        : '依輸入順序處理，任一筆失敗整批取消。'
    "
    :busy="busy"
    wide
  >
    <!-- 編輯與確認共用固定高度區域，錯誤訊息不推動頁面或彈窗按鈕。 -->
    <form
      id="stock-operation-form"
      ref="formElement"
      class="flex h-[min(400px,55dvh)] min-h-0 flex-col"
      @submit.prevent="review"
    >
      <div class="mb-3 flex shrink-0 items-center justify-between gap-3 text-xs">
        <p class="flex items-center gap-2">
          <span :class="step === 'edit' ? 'font-medium text-primary' : 'text-muted-foreground'"
            >1 編輯內容</span
          ><span class="text-muted-foreground">→</span
          ><span :class="step === 'confirm' ? 'font-medium text-primary' : 'text-muted-foreground'"
            >2 確認異動</span
          >
        </p>
        <span class="text-muted-foreground">{{ rows.length }} / 100 筆</span>
      </div>
      <div class="min-h-0 flex-1 overflow-auto">
        <!-- 只有最終確認會發送 API，Enter 編輯表單也只進入摘要。 -->
        <fieldset v-if="step === 'edit'" :disabled="busy || !ready" class="min-w-[740px] space-y-2">
          <div
            class="grid grid-cols-[30px_130px_1fr_1fr_130px_32px] items-center gap-2 text-[11px] text-muted-foreground"
          >
            <span>順序</span><span>異動類型</span><span>異動股東／出讓人</span><span>受讓股東</span
            ><span>股數（股）</span><span></span>
          </div>
          <div
            v-for="(row, index) in rows"
            :key="row.key"
            :data-operation-row="row.key"
            class="grid grid-cols-[30px_130px_1fr_1fr_130px_32px] items-center gap-2"
          >
            <span class="text-center text-xs text-muted-foreground">{{ index + 1 }}</span>
            <select
              v-model="row.transferType"
              class="erp-input"
              :aria-label="`第${index + 1}筆異動類型`"
            >
              <option value="TRANSFER">股票過戶</option>
              <option value="PLEDGE">質權設定</option>
              <option value="UNPLEDGE">質權撤銷</option>
            </select>
            <select
              v-model="row.fromShareholder"
              class="erp-input"
              :aria-label="`第${index + 1}筆異動股東`"
            >
              <option value="">請選擇股東</option>
              <option
                v-for="candidate in candidates"
                :key="candidate.shareholderId"
                :value="candidate.shareholderId"
              >
                {{ candidate.shareholderId }} {{ candidate.name }}
              </option>
            </select>
            <select
              v-model="row.toShareholder"
              class="erp-input"
              :disabled="row.transferType !== 'TRANSFER'"
              :aria-label="`第${index + 1}筆受讓股東`"
            >
              <option value="">
                {{ row.transferType === 'TRANSFER' ? '請選擇受讓人' : '本人質權異動' }}
              </option>
              <option
                v-for="candidate in candidates"
                :key="candidate.shareholderId"
                :value="candidate.shareholderId"
              >
                {{ candidate.shareholderId }} {{ candidate.name }}
              </option>
            </select>
            <input
              v-model="row.quantity"
              class="erp-input text-right"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              :aria-label="`第${index + 1}筆股數`"
              placeholder="輸入股數"
            />
            <Button
              type="button"
              variant="ghost"
              size="icon-xs"
              :aria-label="`刪除第${index + 1}筆`"
              :disabled="rows.length === 1"
              @click="removeRow(index)"
              ><Trash2 class="size-3.5" aria-hidden="true"
            /></Button>
          </div>
        </fieldset>
        <table v-else class="erp-table min-w-[650px]" aria-label="待確認股權異動摘要">
          <thead class="sticky top-0">
            <tr>
              <th>順序</th>
              <th class="text-left!">異動類型</th>
              <th class="text-left!">異動股東／出讓人</th>
              <th class="text-left!">受讓股東</th>
              <th>股數（股）</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(item, index) in preparedRequest?.items ?? []" :key="index">
              <td>{{ index + 1 }}</td>
              <td class="text-left!">{{ typeLabels[item.transferType] }}</td>
              <td class="text-left!">
                {{ item.fromShareholder }} {{ candidateNames.get(item.fromShareholder) }}
              </td>
              <td class="text-left!">
                {{
                  item.transferType === 'TRANSFER'
                    ? `${item.toShareholder} ${candidateNames.get(item.toShareholder ?? '') ?? ''}`
                    : '本人質權異動'
                }}
              </td>
              <td>{{ item.quantity.toLocaleString('zh-TW') }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div
        class="mt-3 h-14 shrink-0 overflow-auto rounded-md px-2 py-1.5 text-xs"
        :class="localError || error ? 'bg-danger-muted text-destructive' : 'text-muted-foreground'"
      >
        <p v-if="localError || error" role="alert" class="whitespace-pre-line">
          {{ localError || error }}
        </p>
      </div>
    </form>
    <template #footer>
      <Button
        v-if="step === 'edit'"
        type="button"
        variant="outline"
        size="sm"
        class="mr-auto"
        :disabled="busy || !ready || rows.length >= 100"
        @click="addRow"
        ><Plus class="size-3.5" aria-hidden="true" />新增一筆</Button
      >
      <Button
        v-else
        type="button"
        variant="outline"
        size="sm"
        class="mr-auto"
        :disabled="busy"
        @click="returnToEdit"
        >返回修改</Button
      >
      <Button type="button" variant="outline" size="sm" :disabled="busy" @click="open = false"
        >取消</Button
      >
      <Button
        v-if="step === 'edit'"
        type="submit"
        form="stock-operation-form"
        size="sm"
        :disabled="!canSubmit"
        >下一步：確認內容</Button
      >
      <Button
        v-else
        ref="confirmButton"
        type="button"
        size="sm"
        :disabled="!canSubmit"
        @click="submitConfirmed"
        ><LoaderCircle v-if="busy" class="size-3.5 animate-spin" aria-hidden="true" />{{
          busy ? '處理中…' : '確認送出'
        }}</Button
      >
    </template>
  </StockDialog>
</template>
