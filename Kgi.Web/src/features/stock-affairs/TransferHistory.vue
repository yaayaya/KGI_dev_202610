<script setup lang="ts">
import { computed, ref } from 'vue';
import type { StockTransferResponse } from '@/api/generated/kgiApi.schemas';
import { formatStockTime } from './stockAffairsApi';

/**
 * 股權異動稽核清單，呈現交易前後股數、操作者及請求識別碼供查核。
 */
const props = defineProps<{ records: StockTransferResponse[]; loading: boolean }>();
const keyword = ref('');
const typeLabels: Record<string, string> = {
  TRANSFER: '股票過戶',
  PLEDGE: '質權設定',
  UNPLEDGE: '質權撤銷',
};
const visibleRows = computed(() => {
  const text = keyword.value.trim().toLowerCase();
  return props.records.filter((row) =>
    [row.fromShareholder, row.toShareholder, row.requestId].some((value) =>
      value.toLowerCase().includes(text),
    ),
  );
});
</script>

<template>
  <section
    class="erp-panel flex h-full min-h-0 flex-col overflow-hidden"
    aria-labelledby="stock-history-title"
    :aria-busy="loading"
  >
    <!-- 請求識別碼可核對結果不明的寫入，時間固定顯示台北時區。 -->
    <div class="erp-panel-heading shrink-0 flex-wrap py-2">
      <h2 id="stock-history-title">
        股權異動紀錄
        <span class="font-normal text-muted-foreground">{{ visibleRows.length }} 筆</span>
      </h2>
      <input
        v-model="keyword"
        class="erp-input w-56!"
        type="search"
        placeholder="股東代碼／請求編號"
        aria-label="搜尋異動紀錄"
      />
    </div>
    <div class="min-h-0 flex-1 overflow-auto">
      <table class="erp-table min-w-[1100px]">
        <thead class="sticky top-0 z-10">
          <tr>
            <th>操作時間</th>
            <th class="text-left!">類型</th>
            <th class="text-left!">異動股東／出讓人</th>
            <th class="text-left!">受讓人</th>
            <th>股數（股）</th>
            <th>出讓人持股前 → 後</th>
            <th>受讓人持股前 → 後</th>
            <th>本人質押前 → 後</th>
            <th class="text-left!">操作者</th>
            <th class="text-left!">請求編號</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in visibleRows" :key="row.transferId">
            <td>{{ formatStockTime(row.createdTime) }}</td>
            <td class="text-left!">{{ typeLabels[row.transferType] ?? row.transferType }}</td>
            <td class="text-left! font-medium">{{ row.fromShareholder }}</td>
            <td class="text-left!">
              {{ row.transferType === 'TRANSFER' ? row.toShareholder : '本人' }}
            </td>
            <td>{{ row.transferQty.toLocaleString('zh-TW') }}</td>
            <td>
              {{ row.fromShareQtyBefore.toLocaleString('zh-TW') }} →
              {{ row.fromShareQtyAfter.toLocaleString('zh-TW') }}
            </td>
            <td>
              {{
                row.transferType === 'TRANSFER'
                  ? `${row.toShareQtyBefore.toLocaleString('zh-TW')} → ${row.toShareQtyAfter.toLocaleString('zh-TW')}`
                  : '—'
              }}
            </td>
            <td>
              {{ row.fromPledgeQtyBefore.toLocaleString('zh-TW') }} →
              {{ row.fromPledgeQtyAfter.toLocaleString('zh-TW') }}
            </td>
            <td class="text-left!">{{ row.operatorId }}</td>
            <td class="text-left! font-mono text-[11px]" :title="row.requestId">
              {{ row.requestId }}
            </td>
          </tr>
          <tr v-if="!visibleRows.length">
            <td colspan="10" class="h-24! text-center! text-muted-foreground">
              {{ loading ? '載入異動紀錄中…' : '沒有符合條件的異動紀錄' }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
