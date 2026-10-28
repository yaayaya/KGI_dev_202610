<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight, LoaderCircle } from '@lucide/vue';
import type { LendingContractResponse } from '@/api/generated/kgiApi.schemas';
import { Button } from '@/components/ui/button';
import HelpHint from '@/components/HelpHint.vue';

/**
 * 監控清單只負責本地篩選、分頁及狀態呈現；維持率與狀態直接沿用後端結果。
 */
const props = defineProps<{
  contracts: LendingContractResponse[];
  loading: boolean;
  failed: boolean;
}>();
const abnormalOnly = defineModel<boolean>('abnormalOnly', { required: true });

// 條件只套用已載入的明細；代碼始終以字串比對，保留前導零。
const contractKeyword = ref('');
const stockKeyword = ref('');
const statusFilter = ref<'All' | LendingContractResponse['status']>('All');
const page = ref(1);
const pageSize = ref(50);
const tableBody = ref<HTMLElement | null>(null);

const hasFilters = computed(
  () =>
    !!contractKeyword.value ||
    !!stockKeyword.value ||
    statusFilter.value !== 'All' ||
    abnormalOnly.value,
);
const filteredContracts = computed(() => {
  const contractText = contractKeyword.value.trim().toLowerCase();
  const stockText = stockKeyword.value.trim().toLowerCase();
  return props.contracts.filter(
    (contract) =>
      contract.contractNo.toLowerCase().includes(contractText) &&
      contract.stockId.toLowerCase().includes(stockText) &&
      (statusFilter.value === 'All' || contract.status === statusFilter.value) &&
      (!abnormalOnly.value || contract.status === 'Warning' || contract.status === 'Call'),
  );
});
const pageCount = computed(() =>
  Math.max(1, Math.ceil(filteredContracts.value.length / pageSize.value)),
);
const visibleContracts = computed(() =>
  filteredContracts.value.slice((page.value - 1) * pageSize.value, page.value * pageSize.value),
);
const startRow = computed(() =>
  filteredContracts.value.length ? (page.value - 1) * pageSize.value + 1 : 0,
);
const endRow = computed(() =>
  Math.min(page.value * pageSize.value, filteredContracts.value.length),
);
const statusLabels: Record<string, string> = {
  Normal: '正常',
  Warning: '預警',
  Call: '處置',
  Unpriced: '待補價',
};

/**
 * 顏色取決於後端狀態，不能由顯示後已四捨五入的比例反推，避免臨界值誤判。
 */
function ratioClass(status: string) {
  if (status === 'Call') return 'bg-danger-muted text-destructive';
  if (status === 'Warning') return 'bg-warning-muted text-warning';
  if (status === 'Normal') return 'bg-success-muted text-success';
  return 'bg-muted text-muted-foreground';
}

/**
 * 清除全部明細條件，保留使用者選擇的每頁筆數。
 */
function clearFilters() {
  contractKeyword.value = '';
  stockKeyword.value = '';
  statusFilter.value = 'All';
  abnormalOnly.value = false;
}

/**
 * 明確選取狀態時關閉異常開關，避免兩種狀態條件形成互斥交集。
 */
function selectStatus(event: Event) {
  const selectedStatus = (event.target as HTMLSelectElement).value as typeof statusFilter.value;
  statusFilter.value = selectedStatus;
  if (selectedStatus !== 'All') abnormalOnly.value = false;
}

// 異常開關也可能由上層變更；只單向重設狀態，不讓兩個條件互相監聽寫入。
watch(abnormalOnly, (enabled) => {
  if (enabled) statusFilter.value = 'All';
});

// 新快照、條件與每頁筆數一律回第一頁，避免停在已不存在的頁碼。
const paginationResetSources = [
  () => props.contracts,
  contractKeyword,
  stockKeyword,
  statusFilter,
  abnormalOnly,
  pageSize,
] as const;
watch(paginationResetSources, () => {
  page.value = 1;
});

// 等資料列更新後只回到表身頂端；即使原本就在第一頁也要處理，並保留水平位置。
watch(
  [...paginationResetSources, page],
  () => {
    if (tableBody.value) tableBody.value.scrollTop = 0;
  },
  { flush: 'post' },
);
</script>

<template>
  <section
    class="erp-panel flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden"
    aria-labelledby="lending-contracts-title"
    :aria-busy="loading"
  >
    <!-- 全體統計由上層保留；這裡的條件只篩選明細。 -->
    <div class="erp-panel-heading flex-wrap justify-start! gap-x-3 gap-y-2 py-2">
      <h2 id="lending-contracts-title" class="flex shrink-0 items-center gap-1.5">
        合約監控
        <HelpHint label="維持率公式與狀態門檻">
          <p>維持率＝擔保品總市值 ÷（借出張數 × 1000 × 收盤價）× 100%。</p>
          <p class="mt-1">
            低於 120% 為處置；120% 以上、低於 140% 為預警；140%
            以上為正常。未取得當日股價時不列為正常。
          </p>
        </HelpHint>
      </h2>
      <label
        for="lending-contract-keyword"
        class="inline-flex items-center gap-1.5 text-xs font-normal"
      >
        <span class="whitespace-nowrap">合約編號</span>
        <input
          id="lending-contract-keyword"
          v-model="contractKeyword"
          type="search"
          autocomplete="off"
          placeholder="輸入關鍵字"
          class="erp-input h-7 w-28"
        />
      </label>
      <label
        for="lending-stock-keyword"
        class="inline-flex items-center gap-1.5 text-xs font-normal"
      >
        <span class="whitespace-nowrap">股票代碼</span>
        <input
          id="lending-stock-keyword"
          v-model="stockKeyword"
          type="search"
          autocomplete="off"
          placeholder="輸入關鍵字"
          class="erp-input h-7 w-28"
        />
      </label>
      <label
        for="lending-status-filter"
        class="inline-flex items-center gap-1.5 text-xs font-normal"
      >
        <span class="whitespace-nowrap">狀態</span>
        <select
          id="lending-status-filter"
          :value="statusFilter"
          class="erp-input h-7 w-23"
          @change="selectStatus"
        >
          <option value="All">全部</option>
          <option v-for="(label, status) in statusLabels" :key="status" :value="status">
            {{ label }}
          </option>
        </select>
      </label>
      <label
        class="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap text-xs font-normal"
        title="僅顯示預警與處置合約"
      >
        <input
          v-model="abnormalOnly"
          type="checkbox"
          role="switch"
          :aria-checked="abnormalOnly"
          class="size-4 accent-primary"
        />
        僅顯示異常
      </label>
      <Button
        type="button"
        variant="outline"
        size="xs"
        :disabled="!hasFilters"
        @click="clearFilters"
        >清除條件</Button
      >
    </div>

    <!-- 表身使用剩餘高度獨立捲動；固定表頭採實色，分離邊框避免捲動時透出資料列。 -->
    <div ref="tableBody" class="isolate min-h-0 min-w-0 flex-1 overflow-auto">
      <table
        class="erp-table min-w-[850px] border-separate border-spacing-0 text-[13px]"
        aria-label="借券合約維持率監控明細"
      >
        <thead class="sticky top-0 z-10 bg-muted [&_th]:bg-muted">
          <tr>
            <th scope="col" class="text-left!">合約編號</th>
            <th scope="col" class="text-left!">股票代碼</th>
            <th scope="col">借出數量（張）</th>
            <th scope="col">擔保品市值（元）</th>
            <th scope="col">收盤價（元）</th>
            <th scope="col">維持率（%）</th>
            <th scope="col" class="text-center!">狀態</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="contract in visibleContracts" :key="contract.contractNo">
            <td class="text-left! font-medium">{{ contract.contractNo }}</td>
            <td class="text-left!">{{ contract.stockId }}</td>
            <td>{{ contract.borrowedQty.toLocaleString('zh-TW') }}</td>
            <td>{{ contract.collateralValue }}</td>
            <td>{{ contract.closingPrice ?? '—' }}</td>
            <td class="font-semibold" :class="ratioClass(contract.status)">
              {{ contract.maintenanceRatio ?? '—' }}
            </td>
            <td class="text-center!">
              <span
                class="inline-flex min-w-11 justify-center rounded px-1.5 py-0.5 text-[11px] font-medium"
                :class="ratioClass(contract.status)"
              >
                {{ statusLabels[contract.status] ?? contract.status }}
              </span>
            </td>
          </tr>
          <tr v-if="visibleContracts.length === 0">
            <td colspan="7" class="h-28! text-center! text-muted-foreground">
              <div class="flex flex-col items-center gap-2">
                <span v-if="loading" class="inline-flex items-center gap-2" role="status"
                  ><LoaderCircle
                    class="size-4 animate-spin"
                    aria-hidden="true"
                  />載入監控資料中</span
                >
                <span v-else-if="failed">監控資料載入失敗，請使用上方重新整理重試。</span>
                <span v-else-if="!contracts.length">目前沒有借券合約。</span>
                <span v-else>沒有符合目前條件的合約。</span>
                <Button
                  v-if="!loading && hasFilters"
                  type="button"
                  variant="outline"
                  size="xs"
                  @click="clearFilters"
                  >清除條件</Button
                >
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- 約千筆資料以本地分頁降低畫面密度，不改變全體件數統計。 -->
    <div
      class="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t px-3 py-2 text-[11px] text-muted-foreground"
    >
      <span aria-live="polite"
        >顯示 {{ startRow }}–{{ endRow }} 筆，篩選後
        {{ filteredContracts.length.toLocaleString('zh-TW') }}／全部
        {{ contracts.length.toLocaleString('zh-TW') }} 筆</span
      >
      <div class="flex flex-wrap items-center gap-3">
        <label for="lending-page-size" class="inline-flex items-center gap-1.5">
          每頁
          <select id="lending-page-size" v-model.number="pageSize" class="erp-input h-7 w-17">
            <option :value="25">25</option>
            <option :value="50">50</option>
            <option :value="100">100</option>
          </select>
          筆
        </label>
        <nav class="flex items-center gap-1.5" aria-label="合約清單分頁">
          <Button
            type="button"
            variant="outline"
            size="icon-xs"
            :disabled="page <= 1"
            aria-label="第一頁合約"
            @click="page = 1"
            ><ChevronsLeft class="size-3.5" aria-hidden="true"
          /></Button>
          <Button
            type="button"
            variant="outline"
            size="icon-xs"
            :disabled="page <= 1"
            aria-label="上一頁合約"
            @click="page -= 1"
            ><ChevronLeft class="size-3.5" aria-hidden="true"
          /></Button>
          <span class="whitespace-nowrap px-1 tabular-nums">第 {{ page }}／{{ pageCount }} 頁</span>
          <Button
            type="button"
            variant="outline"
            size="icon-xs"
            :disabled="page >= pageCount"
            aria-label="下一頁合約"
            @click="page += 1"
            ><ChevronRight class="size-3.5" aria-hidden="true"
          /></Button>
          <Button
            type="button"
            variant="outline"
            size="icon-xs"
            :disabled="page >= pageCount"
            aria-label="最後一頁合約"
            @click="page = pageCount"
            ><ChevronsRight class="size-3.5" aria-hidden="true"
          /></Button>
        </nav>
      </div>
    </div>
  </section>
</template>
