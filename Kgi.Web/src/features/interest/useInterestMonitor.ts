import axios from 'axios';
import { computed, onMounted, onScopeDispose, ref, shallowRef } from 'vue';
import type {
  InterestBatchDetailResponse,
  InterestBatchResponse,
} from '@/api/generated/kgiApi.schemas';
import { getInterestErrorMessage, interestApi } from './interestApi';

/**
 * 管理 Q3 五秒監控與啟動指令；背景讀取保留快照，結果不明的寫入必須先查回狀態。
 */
export function useInterestMonitor() {
  // 批次與明細各自保留錯誤，斷線不改寫後端業務狀態。
  const batches = shallowRef<InterestBatchResponse[]>([]);
  const selectedBatchId = ref('');
  const detail = shallowRef<InterestBatchDetailResponse | null>(null);
  const refreshing = ref(false);
  const loaded = ref(false);
  const listError = ref('');
  const detailError = ref('');
  const lastUpdatedTime = ref<string | null>(null);
  const startingIds = ref(new Set<string>());
  const unknownIds = ref(new Set<string>());
  const startErrors = ref<Record<string, string>>({});
  const initialLoading = computed(() => refreshing.value && !loaded.value);
  const selectedBatch = computed(
    () => batches.value.find((batch) => batch.batchId === selectedBatchId.value) ?? null,
  );
  const selectedDetail = computed(() =>
    detail.value?.batch.batchId === selectedBatchId.value ? detail.value : null,
  );
  const startError = computed(() => startErrors.value[selectedBatchId.value] ?? '');

  let active = true;
  let readVersion = 0;
  let selectionVersion = 0;
  let reloadRequested = false;
  let readController: AbortController | undefined;
  let pollTimer: ReturnType<typeof setInterval> | undefined;

  /**
   * 只有啟動請求結束後發起的成功讀取，才能解除「結果待確認」的再次啟動限制。
   */
  function confirmUnknown(batchIds: string[]) {
    for (const batchId of batchIds) {
      if (unknownIds.value.delete(batchId)) delete startErrors.value[batchId];
    }
  }

  /**
   * 同一時間只讀取一組清單及明細；人工操作需要刷新時合併到目前讀取完成之後。
   */
  async function reloadMonitor(queueIfBusy = true): Promise<void> {
    if (!active) return;
    if (refreshing.value) {
      if (queueIfBusy) reloadRequested = true;
      return;
    }
    refreshing.value = true;
    const version = readVersion;
    const controller = new AbortController();
    readController = controller;

    try {
      try {
        const result = await interestApi.getBatches(controller.signal);
        if (!active || version !== readVersion) return;
        batches.value = result;
        loaded.value = true;
        listError.value = '';
        lastUpdatedTime.value = new Date().toISOString();
        confirmUnknown(result.map((batch) => batch.batchId));
        if (!result.some((batch) => batch.batchId === selectedBatchId.value)) {
          selectedBatchId.value = result[0]?.batchId ?? '';
          selectionVersion += 1;
          detail.value = null;
          detailError.value = '';
        }
      } catch (error) {
        if (!active || version !== readVersion || axios.isCancel(error)) return;
        listError.value = getInterestErrorMessage(
          error,
          '無法更新批次清單，請確認 API 連線後重試。',
        );
      }

      const batchId = selectedBatchId.value;
      const selectedVersion = selectionVersion;
      if (!batchId || !active || version !== readVersion) return;
      try {
        const result = await interestApi.getBatch(batchId, controller.signal);
        if (
          !active ||
          version !== readVersion ||
          selectedVersion !== selectionVersion ||
          batchId !== selectedBatchId.value
        )
          return;
        detail.value = result;
        detailError.value = '';
        batches.value = batches.value.map((batch) =>
          batch.batchId === batchId ? result.batch : batch,
        );
        confirmUnknown([batchId]);
      } catch (error) {
        if (
          active &&
          version === readVersion &&
          selectedVersion === selectionVersion &&
          batchId === selectedBatchId.value &&
          !axios.isCancel(error)
        ) {
          detailError.value = getInterestErrorMessage(
            error,
            '無法更新批次明細，顯示內容可能尚未更新。',
          );
        }
      }
    } finally {
      if (active) {
        refreshing.value = false;
        if (reloadRequested) {
          reloadRequested = false;
          void reloadMonitor();
        }
      }
    }
  }

  /**
   * 切換批次立即清除前一批明細；輪詢更新不重新選取或重設捲動位置。
   */
  function selectBatch(batchId: string) {
    if (!active || batchId === selectedBatchId.value) return;
    selectedBatchId.value = batchId;
    selectionVersion += 1;
    detail.value = null;
    detailError.value = '';
    void reloadMonitor();
  }

  /**
   * 僅待處理批次可以啟動；網路結果不明時鎖住該批，查回狀態前不允許再次送出。
   */
  async function startSettlement(batchId: string): Promise<InterestBatchResponse | null> {
    const batch = batches.value.find((item) => item.batchId === batchId);
    if (
      !active ||
      !batch ||
      batch.processStatus !== 0 ||
      startingIds.value.has(batchId) ||
      unknownIds.value.has(batchId)
    )
      return null;

    startingIds.value.add(batchId);
    delete startErrors.value[batchId];
    readVersion += 1;
    selectedBatchId.value = batchId;
    selectionVersion += 1;
    detail.value = null;
    detailError.value = '';

    try {
      const result = await interestApi.startSettlement(batchId);
      if (!active) return null;
      batches.value = batches.value.map((item) => (item.batchId === batchId ? result : item));
      return result;
    } catch (error) {
      if (!active) return null;
      const uncertain =
        !axios.isAxiosError(error) || !error.response || error.response.status >= 500;
      if (uncertain) unknownIds.value.add(batchId);
      startErrors.value[batchId] = getInterestErrorMessage(
        error,
        uncertain
          ? '無法確認啟動結果，正在查詢批次狀態；確認前請勿重複送出。'
          : '無法啟動結算，請確認批次狀態後再操作。',
      );
      return null;
    } finally {
      if (active) {
        startingIds.value.delete(batchId);
        readVersion += 1;
        void reloadMonitor();
      }
    }
  }

  onMounted(() => {
    void reloadMonitor();
    pollTimer = setInterval(() => void reloadMonitor(false), 5_000);
  });
  onScopeDispose(() => {
    active = false;
    readVersion += 1;
    readController?.abort();
    if (pollTimer) clearInterval(pollTimer);
  });

  return {
    batches,
    selectedBatchId,
    selectedBatch,
    selectedDetail,
    refreshing,
    initialLoading,
    listError,
    detailError,
    startError,
    lastUpdatedTime,
    startingIds,
    unknownIds,
    reloadMonitor,
    selectBatch,
    startSettlement,
  };
}
