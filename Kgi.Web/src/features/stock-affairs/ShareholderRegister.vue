<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ChevronLeft, ChevronRight } from '@lucide/vue';
import type { StockShareholderResponse } from '@/api/generated/kgiApi.schemas';
import { Button } from '@/components/ui/button';

/**
 * 目前名簿的查詢、排序及分頁；股數與可用數量直接呈現後端結果。
 */
const props = defineProps<{
  shareholders: StockShareholderResponse[];
  loading: boolean;
  disabled: boolean;
}>();
const emit = defineEmits<{ operate: [shareholderId: string] }>();
const keyword = ref('');
const order = ref('id');
const positiveOnly = ref(false);
const page = ref(1);
const pageSize = ref(25);
const scrollBody = ref<HTMLElement | null>(null);
const filtered = computed(() => {
  const text = keyword.value.trim().toLowerCase();
  const rows = props.shareholders.filter(
    (row) =>
      (!positiveOnly.value || row.shareQty > 0) &&
      (row.shareholderId.toLowerCase().includes(text) || row.name.toLowerCase().includes(text)),
  );
  return rows.sort((a, b) => {
    if (order.value === 'name')
      return (
        a.name.localeCompare(b.name, 'zh-TW') || a.shareholderId.localeCompare(b.shareholderId)
      );
    if (order.value === 'shares')
      return b.shareQty - a.shareQty || a.shareholderId.localeCompare(b.shareholderId);
    if (order.value === 'pledge')
      return b.pledgeQty - a.pledgeQty || a.shareholderId.localeCompare(b.shareholderId);
    return a.shareholderId.localeCompare(b.shareholderId);
  });
});
const pageCount = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize.value)));
const visibleRows = computed(() =>
  filtered.value.slice((page.value - 1) * pageSize.value, page.value * pageSize.value),
);
const start = computed(() => (filtered.value.length ? (page.value - 1) * pageSize.value + 1 : 0));
const end = computed(() => Math.min(page.value * pageSize.value, filtered.value.length));

// 條件及新資料回第一頁；表身回頂端但保留水平捲動位置。
const resetSources = [() => props.shareholders, keyword, order, positiveOnly, pageSize] as const;
watch(resetSources, () => {
  page.value = 1;
});
watch(
  [...resetSources, page],
  () => {
    if (scrollBody.value) scrollBody.value.scrollTop = 0;
  },
  { flush: 'post' },
);
</script>

<template>
  <section
    class="erp-panel flex h-full min-h-0 flex-col overflow-hidden"
    aria-labelledby="stock-register-title"
    :aria-busy="loading"
  >
    <!-- 明細條件只影響名簿，不改變頂部公司統計。 -->
    <div class="erp-panel-heading shrink-0 flex-wrap py-2">
      <h2 id="stock-register-title">
        股東名簿 <span class="font-normal text-muted-foreground">{{ shareholders.length }} 筆</span>
      </h2>
      <div class="flex flex-wrap items-center gap-2">
        <input
          v-model="keyword"
          type="search"
          class="erp-input w-44!"
          aria-label="搜尋股東代碼或姓名"
          placeholder="代碼／姓名"
        />
        <select v-model="order" class="erp-input w-32!" aria-label="名簿排序">
          <option value="id">代碼由小到大</option>
          <option value="name">姓名排序</option>
          <option value="shares">持股由多到少</option>
          <option value="pledge">質押由多到少</option>
        </select>
        <label class="inline-flex items-center gap-1.5 font-normal"
          ><input v-model="positiveOnly" type="checkbox" class="accent-primary" />僅正持股</label
        >
      </div>
    </div>
    <!-- 寬表僅在表身橫向捲動，分頁始終留在下方。 -->
    <div ref="scrollBody" class="min-h-0 flex-1 overflow-auto">
      <table class="erp-table min-w-[850px]">
        <thead class="sticky top-0 z-10 bg-muted">
          <tr>
            <th class="text-left!">股東代碼</th>
            <th class="text-left!">姓名</th>
            <th class="text-left!">身分識別</th>
            <th>持股（股）</th>
            <th>已質押（股）</th>
            <th>可用（股）</th>
            <th class="text-left!">通訊地址</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in visibleRows" :key="row.shareholderId">
            <td class="text-left! font-medium">{{ row.shareholderId }}</td>
            <td class="text-left!">{{ row.name }}</td>
            <td class="text-left! text-muted-foreground">{{ row.maskedIdNo }}</td>
            <td>{{ row.shareQty.toLocaleString('zh-TW') }}</td>
            <td>{{ row.pledgeQty.toLocaleString('zh-TW') }}</td>
            <td :class="row.availableQty === 0 ? 'text-muted-foreground' : 'font-medium'">
              {{ row.availableQty.toLocaleString('zh-TW') }}
            </td>
            <td class="max-w-64 truncate text-left!" :title="row.address">{{ row.address }}</td>
            <td>
              <Button
                type="button"
                variant="ghost"
                size="xs"
                :disabled="disabled || loading"
                :aria-label="`${row.name} 股權異動`"
                @click="emit('operate', row.shareholderId)"
                >異動</Button
              >
            </td>
          </tr>
          <tr v-if="!visibleRows.length">
            <td colspan="8" class="h-24! text-center! text-muted-foreground">
              {{ loading ? '載入名簿中…' : '沒有符合條件的股東' }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t px-3 py-2 text-xs">
      <p class="text-muted-foreground">第 {{ start }}–{{ end }} 筆，共 {{ filtered.length }} 筆</p>
      <div class="flex items-center gap-2">
        <select v-model="pageSize" class="erp-input w-24!" aria-label="每頁筆數">
          <option :value="25">25 筆／頁</option>
          <option :value="50">50 筆／頁</option>
          <option :value="100">100 筆／頁</option>
        </select>
        <Button type="button" variant="outline" size="xs" :disabled="page === 1" @click="page = 1"
          >首頁</Button
        >
        <Button
          type="button"
          variant="outline"
          size="icon-xs"
          aria-label="上一頁"
          :disabled="page === 1"
          @click="page--"
          ><ChevronLeft class="size-3.5"
        /></Button>
        <span class="tabular-nums">{{ page }} / {{ pageCount }}</span>
        <Button
          type="button"
          variant="outline"
          size="icon-xs"
          aria-label="下一頁"
          :disabled="page === pageCount"
          @click="page++"
          ><ChevronRight class="size-3.5"
        /></Button>
        <Button
          type="button"
          variant="outline"
          size="xs"
          :disabled="page === pageCount"
          @click="page = pageCount"
          >末頁</Button
        >
      </div>
    </div>
  </section>
</template>
