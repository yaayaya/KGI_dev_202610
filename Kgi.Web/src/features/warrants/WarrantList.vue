<script setup lang="ts">
import { computed, nextTick, ref } from 'vue';
import { LoaderCircle, RefreshCw, Search, X } from '@lucide/vue';
import { Button } from '@/components/ui/button';
import type { WarrantResponse } from './warrantTypes';

/**
 * 權證導覽清單：顯示父頁提供的資料，將選券與重試事件交回父頁處理。
 */
const props = defineProps<{
  warrants: WarrantResponse[];
  selectedId?: string;
  loading: boolean;
  error: string;
  disabled?: boolean;
}>();
const emit = defineEmits<{
  select: [warrant: WarrantResponse];
  retry: [];
}>();

// 查詢條件只影響清單顯示，不變更目前選券或右側試算內容。
const keyword = ref('');
const typeFilter = ref<'ALL' | WarrantResponse['warrantType']>('ALL');
const typeOptions = [
  { value: 'ALL', label: '全部' },
  { value: 'CALL', label: 'CALL' },
  { value: 'PUT', label: 'PUT' },
] as const;

// DOM 參照供清除搜尋、手機返回清單及鍵盤導覽時恢復焦點。
const searchInput = ref<HTMLInputElement>();
const list = ref<HTMLElement>();

// 代碼與類型採交集篩選；目前選券不在結果中時，讓第一筆仍可用 Tab 進入。
const hasFilter = computed(() => keyword.value.trim() !== '' || typeFilter.value !== 'ALL');
const filtered = computed(() => {
  const text = keyword.value.trim().toUpperCase();
  return props.warrants.filter(
    (item) =>
      item.warrantId.toUpperCase().includes(text) &&
      (typeFilter.value === 'ALL' || item.warrantType === typeFilter.value),
  );
});
const selectedVisible = computed(() =>
  filtered.value.some((item) => item.warrantId === props.selectedId),
);

/**
 * 供父頁從手機明細返回時，將焦點還給選券；選券被篩除時改停在第一筆。
 */
function focusSelection() {
  const target =
    list.value?.querySelector<HTMLButtonElement>('[aria-current="true"]') ??
    list.value?.querySelector<HTMLButtonElement>('[data-warrant]');
  target?.focus({ preventScroll: true });
}

defineExpose({ focusSelection });

/**
 * 只清除代碼條件並恢復輸入焦點，保留使用者選擇的 CALL／PUT 篩選。
 */
function clearSearch() {
  keyword.value = '';
  searchInput.value?.focus();
}

/**
 * 以方向鍵及 Home／End 切券，維持單一 Tab 停駐點，避免逐筆走過整份清單。
 */
async function navigate(event: KeyboardEvent, index: number) {
  if (props.disabled) return;
  const last = filtered.value.length - 1;
  const next = {
    ArrowDown: Math.min(index + 1, last),
    ArrowUp: Math.max(index - 1, 0),
    Home: 0,
    End: last,
  }[event.key];
  if (next === undefined) return;
  event.preventDefault();
  const warrant = filtered.value[next];
  if (!warrant) return;
  emit('select', warrant);
  await nextTick();
  const target = list.value?.querySelectorAll<HTMLButtonElement>('[data-warrant]')[next];
  target?.focus({ preventScroll: true });
  target?.scrollIntoView?.({ block: 'nearest' });
}
</script>

<template>
  <section
    class="erp-panel flex min-h-0 flex-col overflow-hidden"
    aria-labelledby="warrant-list-title"
  >
    <!-- 清單標題與筆數：有查詢條件時同時顯示符合筆數及總筆數。 -->
    <div class="erp-panel-heading">
      <h2 id="warrant-list-title">權證清單</h2>
      <span class="text-[11px] font-normal text-muted-foreground tabular-nums">
        {{ hasFilter ? `${filtered.length} / ${warrants.length}` : warrants.length }} 筆
      </span>
    </div>

    <!-- 查詢區：代碼搜尋與類型篩選共用同一份已載入資料。 -->
    <div class="relative m-2 shrink-0">
      <label for="warrant-search" class="sr-only">搜尋權證代碼</label>
      <Search
        class="pointer-events-none absolute top-2.5 left-2 size-3.5 text-muted-foreground"
        aria-hidden="true"
      />
      <input
        ref="searchInput"
        id="warrant-search"
        v-model="keyword"
        type="search"
        class="erp-input pr-7 pl-7"
        placeholder="搜尋權證代碼"
        autocomplete="off"
      />
      <button
        v-if="keyword"
        type="button"
        class="absolute top-1 right-1 flex size-6 items-center justify-center rounded hover:bg-muted"
        aria-label="清除代碼搜尋"
        @click="clearSearch"
      >
        <X class="size-3.5" aria-hidden="true" />
      </button>
    </div>
    <div
      class="mx-2 mb-2 flex shrink-0 gap-1 rounded-md border bg-muted/50 p-0.5"
      role="group"
      aria-label="權證類型篩選"
    >
      <button
        v-for="option in typeOptions"
        :key="option.value"
        type="button"
        :aria-pressed="typeFilter === option.value"
        :disabled="disabled"
        :class="[
          'h-7 flex-1 rounded px-2 text-[11px] font-medium transition-colors motion-reduce:transition-none disabled:cursor-wait disabled:opacity-60',
          typeFilter === option.value
            ? option.value === 'PUT'
              ? 'bg-secondary text-secondary-foreground'
              : 'bg-accent text-accent-foreground'
            : 'text-muted-foreground hover:bg-card hover:text-foreground',
        ]"
        @click="typeFilter = option.value"
      >
        {{ option.label }}
      </button>
    </div>
    <!-- 查詢區結束。 -->

    <div
      class="grid shrink-0 grid-cols-[1fr_48px] border-y bg-muted/50 px-3 py-1.5 text-[11px] text-muted-foreground"
      aria-hidden="true"
    >
      <span>權證代碼</span><span class="text-right">類型</span>
    </div>

    <!-- 清單狀態：載入、錯誤、空結果與資料列互斥；取資料及重試由父頁負責。 -->
    <div
      v-if="loading"
      class="flex items-center justify-center gap-2 p-5 text-xs text-muted-foreground"
      role="status"
    >
      <LoaderCircle class="size-4 animate-spin" aria-hidden="true" />載入權證中
    </div>
    <div v-else-if="error" class="space-y-2 p-3">
      <p class="text-xs text-destructive" role="alert">{{ error }}</p>
      <Button type="button" variant="outline" size="sm" @click="emit('retry')"
        ><RefreshCw aria-hidden="true" />重新載入</Button
      >
    </div>
    <div
      v-else-if="filtered.length === 0"
      class="p-5 text-center text-xs text-muted-foreground"
      role="status"
    >
      {{ hasFilter ? '沒有符合的權證' : '目前沒有權證資料' }}
    </div>
    <ul
      v-else
      ref="list"
      class="min-h-0 flex-1 overflow-y-auto overscroll-contain"
      aria-label="權證代碼列表"
    >
      <!-- 資料列只發出選券事件，切券後的 API 查詢與試算重置不在此元件處理。 -->
      <li v-for="(warrant, index) in filtered" :key="warrant.warrantId">
        <button
          type="button"
          data-warrant
          :disabled="disabled"
          :aria-current="selectedId === warrant.warrantId ? 'true' : undefined"
          :tabindex="selectedId === warrant.warrantId || (!selectedVisible && index === 0) ? 0 : -1"
          :class="[
            'group flex h-8 w-full items-center justify-between border-b border-border/45 border-l-2 px-2.5 text-left text-xs tabular-nums hover:bg-muted/60 focus-visible:relative focus-visible:z-10 focus-visible:outline-offset-[-2px] [@media(pointer:coarse)]:h-10 disabled:cursor-wait disabled:opacity-60',
            selectedId === warrant.warrantId
              ? 'border-l-primary bg-accent text-accent-foreground'
              : 'border-l-transparent',
          ]"
          @click="emit('select', warrant)"
          @keydown="navigate($event, index)"
        >
          <span class="font-medium tracking-wide">{{ warrant.warrantId }}</span>
          <span
            :class="[
              'rounded px-1.5 py-0.5 text-[11px] font-medium',
              warrant.warrantType === 'CALL'
                ? 'bg-accent text-accent-foreground'
                : 'bg-secondary text-secondary-foreground',
            ]"
            >{{ warrant.warrantType }}</span
          >
        </button>
      </li>
    </ul>
  </section>
</template>
