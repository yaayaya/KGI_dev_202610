import axios from 'axios';
import { computed, onMounted, onScopeDispose, ref, shallowRef } from 'vue';
import type { LendingMonitorResponse } from '@/api/generated/kgiApi.schemas';
import { getLendingErrorMessage, lendingApi } from './lendingApi';

/**
 * 管理 Q2 行情匯入、重置及監控快照；讀取與寫入錯誤分開，金融計算全部由後端處理。
 */
export function useLendingMonitor() {
  // 監控讀取：重整失敗保留前次資料，並透過錯誤提示標示資料尚未更新。
  const monitor = shallowRef<LendingMonitorResponse | null>(null);
  const loading = ref(false);
  const loadError = ref('');
  const abnormalOnly = ref(false);
  const resetting = ref(false);
  const resetError = ref('');

  // 選檔不變更既有行情；寫入成功前保留檔案，方便修正錯誤或再次檢視。
  const selectedFile = shallowRef<File | null>(null);
  const fileError = ref('');
  const importing = ref(false);
  const importError = ref('');
  const importNotice = ref('');
  const canImport = computed(
    () => !!selectedFile.value && !importing.value && !resetting.value && !fileError.value,
  );

  let active = true;
  let loadVersion = 0;
  let loadController: AbortController | undefined;

  /**
   * 手動重新整理取得最新行情日；匯入後可明確讀取該日，並拒絕過期回應覆蓋畫面。
   */
  async function reloadMonitor(tradeDate?: string): Promise<boolean> {
    if (!active) return false;
    const version = ++loadVersion;
    loadController?.abort();
    const controller = new AbortController();
    loadController = controller;
    loading.value = true;
    loadError.value = '';
    try {
      const result = await lendingApi.getMonitor(tradeDate, controller.signal);
      if (!active || version !== loadVersion) return false;
      monitor.value = result;
      resetError.value = '';
      return true;
    } catch (error) {
      if (active && version === loadVersion && !axios.isCancel(error)) {
        loadError.value = getLendingErrorMessage(
          error,
          '無法載入監控資料，請確認 API 連線後重試。',
        );
      }
      return false;
    } finally {
      if (active && version === loadVersion) loading.value = false;
    }
  }

  /**
   * 只檢查瀏覽器可確定的檔案條件；完整內容、缺價與日期規則由後端驗證。
   */
  function selectFiles(files: File[]) {
    if (importing.value || resetting.value || !active) return;
    selectedFile.value = null;
    fileError.value = '';
    importError.value = '';
    importNotice.value = '';
    if (files.length !== 1) {
      fileError.value = '請一次選擇一個 CSV 檔案。';
      return;
    }
    const file = files[0]!;
    if (!/\.csv$/i.test(file.name)) {
      fileError.value = '請選擇副檔名為 .csv 的檔案。';
    } else if (file.size === 0) {
      fileError.value = '檔案是空的，請重新選擇。';
    } else if (file.size > 2 * 1024 * 1024) {
      fileError.value = 'CSV 檔案不可超過 2 MiB。';
    } else {
      selectedFile.value = file;
    }
  }

  /**
   * 正式匯入不自動重試；寫入成功與後續重新讀取各自處理，避免誤導使用者重複送出。
   */
  async function importFile() {
    const file = selectedFile.value;
    if (!canImport.value || !file || !active) return null;
    importing.value = true;
    importError.value = '';
    importNotice.value = '';
    resetError.value = '';
    loadVersion += 1;
    loadController?.abort();
    loading.value = false;

    try {
      const result = await lendingApi.importFile(file);
      if (!active) return null;
      selectedFile.value = null;
      importNotice.value = `已匯入 ${result.importedCount} 筆行情，交易日 ${result.tradeDate}。`;
      const refreshed = await reloadMonitor(result.tradeDate);
      if (!active) return null;
      if (!refreshed) {
        importNotice.value = '匯入已成功，但監控資料刷新失敗；請重新整理，下方仍為前次載入結果。';
      }
      return result;
    } catch (error) {
      if (active) {
        importError.value = getLendingErrorMessage(
          error,
          '無法確認匯入是否完成，請先重新整理監控資料，再決定是否重試。',
        );
      }
      return null;
    } finally {
      if (active) importing.value = false;
    }
  }

  /**
   * 清除全部已匯入行情後重新讀取；寫入成功與刷新失敗分開處理，不自動重送。
   */
  async function resetMarketPrices(): Promise<boolean> {
    if (!active || loading.value || importing.value || resetting.value) return false;
    resetting.value = true;
    resetError.value = '';
    loadVersion += 1;
    loadController?.abort();

    try {
      await lendingApi.resetMarketPrices();
      if (!active) return false;
      selectedFile.value = null;
      fileError.value = '';
      importError.value = '';
      importNotice.value = '';
      abnormalOnly.value = false;
      const refreshed = await reloadMonitor();
      if (!active) return false;
      if (!refreshed) {
        loadError.value = `行情已重置，但監控資料刷新失敗，請重試。\n${loadError.value}`;
      }
      return true;
    } catch (error) {
      if (active) {
        resetError.value = getLendingErrorMessage(
          error,
          '無法確認行情是否已重置，請先重新整理監控資料。',
        );
      }
      return false;
    } finally {
      if (active) resetting.value = false;
    }
  }

  onMounted(() => void reloadMonitor());
  onScopeDispose(() => {
    active = false;
    loadVersion += 1;
    loadController?.abort();
  });

  return {
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
  };
}
