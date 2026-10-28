import axios from 'axios';
import { computed, onMounted, onScopeDispose, ref, shallowRef, watch } from 'vue';
import { getWarrantErrorMessage, warrantApi } from './warrantApi';
import type { TrialResponse, WarrantResponse } from './warrantTypes';

/**
 * 以字串檢查股價格式及 DECIMAL(18,4) 範圍，避免轉成 JavaScript number 而遺失精度。
 */
export function validateMarketPrice(value: string): string {
  const price = value.trim();
  if (!price) return '';
  if (!/^\d+(?:\.\d{1,4})?$/.test(price)) {
    return '請輸入有效股價，最多四位小數。';
  }
  if (!/[1-9]/.test(price)) return '標的股價必須大於 0。';

  const integer = price.split('.')[0]!.replace(/^0+/, '');
  if (integer.length > 14) return '標的股價不可超過 99999999999999.9999。';
  return '';
}

/**
 * 管理 Q1 選券、預覽、儲存與歷史狀態；畫面只負責呈現，財務計算交由後端 API。
 */
export function useWarrantWorkspace() {
  // 清單與目前操作對象：搜尋、類型篩選由清單元件處理，不改變這裡的選券狀態。
  const warrants = shallowRef<WarrantResponse[]>([]);
  const loadingList = ref(false);
  const listError = ref('');
  const selectedWarrant = shallowRef<WarrantResponse | null>(null);

  // 輸入與預覽：價格及計算結果全程保留 API 的十進位字串。
  const marketPrice = ref('');
  const preview = shallowRef<TrialResponse | null>(null);
  const previewLoading = ref(false);
  const previewError = ref('');

  // 歷史與儲存各有獨立狀態，避免讀取歷史失敗被誤當成儲存失敗。
  const history = shallowRef<TrialResponse[]>([]);
  const historyLoading = ref(false);
  const historyError = ref('');
  const saving = ref(false);
  const saveError = ref('');
  const validationMessage = computed(() => validateMarketPrice(marketPrice.value));
  const priceTouched = computed(() => marketPrice.value.length > 0);

  // 取消舊請求以減少無效工作；世代編號再攔截已經回來、無法及時取消的過期回應。
  let active = true;
  let listVersion = 0;
  let historyVersion = 0;
  let previewVersion = 0;
  let listController: AbortController | undefined;
  let historyController: AbortController | undefined;
  let previewController: AbortController | undefined;
  let previewTimer: ReturnType<typeof setTimeout> | undefined;
  let previewInput = '';

  // 只有目前選券及股價對應的最新預覽可儲存，輸入一變更就不再沿用舊結果。
  const canSave = computed(
    () =>
      !!selectedWarrant.value &&
      !!marketPrice.value.trim() &&
      !validationMessage.value &&
      !!preview.value &&
      !previewLoading.value &&
      !saving.value &&
      preview.value.warrantId === selectedWarrant.value.warrantId &&
      previewInput === marketPrice.value.trim(),
  );

  /**
   * 同時失效舊預覽、取消請求與防抖計時，供換券、改價及離開頁面共用。
   */
  function resetPreview() {
    previewVersion += 1;
    clearTimeout(previewTimer);
    previewController?.abort();
    previewInput = '';
    preview.value = null;
    previewLoading.value = false;
    previewError.value = '';
  }

  /**
   * 重新讀取權證清單；原選券已不存在時，連同輸入、預覽與歷史一起清空。
   */
  async function reloadList() {
    const version = ++listVersion;
    listController?.abort();
    const controller = new AbortController();
    listController = controller;
    loadingList.value = true;
    listError.value = '';
    try {
      const data = await warrantApi.getWarrants(controller.signal);
      if (!active || version !== listVersion) return;
      warrants.value = data;
      if (
        selectedWarrant.value &&
        !data.some((warrant) => warrant.warrantId === selectedWarrant.value?.warrantId)
      ) {
        selectedWarrant.value = null;
        marketPrice.value = '';
        resetPreview();
        historyVersion += 1;
        historyController?.abort();
        history.value = [];
        historyError.value = '';
        historyLoading.value = false;
      }
    } catch (error) {
      if (active && version === listVersion && !axios.isCancel(error)) {
        listError.value = getWarrantErrorMessage(
          error,
          '無法載入權證清單，請確認 API 連線後重試。',
        );
      }
    } finally {
      if (active && version === listVersion) loadingList.value = false;
    }
  }

  /**
   * 讀取目前權證最近十筆紀錄，透過請求世代避免換券後混入前一檔的歷史。
   */
  async function reloadHistory() {
    const warrantId = selectedWarrant.value?.warrantId;
    if (!warrantId || !active) return;
    const version = ++historyVersion;
    historyController?.abort();
    const controller = new AbortController();
    historyController = controller;
    historyLoading.value = true;
    historyError.value = '';
    try {
      const data = await warrantApi.getHistory(warrantId, controller.signal);
      if (!active || version !== historyVersion) return;
      history.value = data;
    } catch (error) {
      if (active && version === historyVersion && !axios.isCancel(error)) {
        historyError.value = getWarrantErrorMessage(error, '無法載入歷史紀錄，請重新載入。');
      }
    } finally {
      if (active && version === historyVersion) historyLoading.value = false;
    }
  }

  /**
   * 切換操作對象時重設前一檔的試算資料，並載入新選券的歷史紀錄。
   */
  function selectWarrant(warrant: WarrantResponse) {
    if (selectedWarrant.value?.warrantId === warrant.warrantId) return;
    selectedWarrant.value = warrant;
    marketPrice.value = '';
    resetPreview();
    saveError.value = '';
    history.value = [];
    void reloadHistory();
  }

  /**
   * 股價有效後預設等待 250ms 再送預覽；重試可略過等待，結果仍須通過世代檢查。
   */
  function queuePreview(delay = 250) {
    resetPreview();
    saveError.value = '';
    const warrantId = selectedWarrant.value?.warrantId;
    const price = marketPrice.value.trim();
    if (!warrantId || !price || validationMessage.value || !active) return;

    const version = previewVersion;
    previewLoading.value = true;
    previewTimer = setTimeout(async () => {
      const controller = new AbortController();
      previewController = controller;
      try {
        const data = await warrantApi.preview(warrantId, price, controller.signal);
        if (!active || version !== previewVersion) return;
        preview.value = data;
        previewInput = price;
      } catch (error) {
        if (active && version === previewVersion && !axios.isCancel(error)) {
          previewError.value = getWarrantErrorMessage(error, '試算暫時失敗，請重試。');
        }
      } finally {
        if (active && version === previewVersion) previewLoading.value = false;
      }
    }, delay);
  }

  /**
   * 成功後清空同一份輸入並更新歷史；失敗保留資料，回傳結果交由畫面決定成功提示。
   */
  async function saveTrial(): Promise<boolean> {
    if (!canSave.value || !selectedWarrant.value) return false;
    const warrantId = selectedWarrant.value.warrantId;
    const price = marketPrice.value.trim();
    const submittedPreviewVersion = previewVersion;
    saving.value = true;
    saveError.value = '';
    try {
      await warrantApi.saveTrial(warrantId, price);
      if (active && selectedWarrant.value?.warrantId === warrantId) {
        // 儲存期間若操作對象或輸入已更新，舊回應不得清掉新一輪的試算內容。
        if (submittedPreviewVersion === previewVersion) marketPrice.value = '';
        void reloadHistory();
      }
      return true;
    } catch (error) {
      if (
        active &&
        selectedWarrant.value?.warrantId === warrantId &&
        submittedPreviewVersion === previewVersion
      ) {
        saveError.value = getWarrantErrorMessage(
          error,
          '無法確認是否已儲存，請先重新載入歷史紀錄再重試。',
        );
      }
      return false;
    } finally {
      if (active) saving.value = false;
    }
  }

  // 同步失效舊預覽，避免輸入剛改變、畫面尚未更新時仍可提交上一份結果。
  watch(marketPrice, () => queuePreview(), { flush: 'sync' });

  // 進入頁面只載入清單；選券由使用者決定，不預設第一檔。
  onMounted(() => void reloadList());

  // 離開頁面時停止所有讀取與計時，後續回應不再寫入已卸載的工作區。
  onScopeDispose(() => {
    active = false;
    listController?.abort();
    historyController?.abort();
    resetPreview();
  });

  // 對畫面提供狀態及操作入口；請求取消、世代與防抖等細節留在本模組內。
  return {
    warrants,
    loadingList,
    listError,
    selectedWarrant,
    marketPrice,
    preview,
    previewLoading,
    previewError,
    validationMessage,
    priceTouched,
    history,
    historyLoading,
    historyError,
    saving,
    saveError,
    canSave,
    selectWarrant,
    reloadList,
    reloadHistory,
    retryPreview: () => queuePreview(0),
    saveTrial,
  };
}
