import axios from 'axios';
import { computed, onMounted, onScopeDispose, ref, shallowRef, watch } from 'vue';
import type {
  StockCompanyResponse,
  StockDividendPreview,
  StockDividendResponse,
  StockMeetingResponse,
  StockNoticesResponse,
  StockRegisterResponse,
  StockTransferRequest,
  StockTransferResponse,
} from '@/api/generated/kgiApi.schemas';
import { stockAffairsApi, stockErrorMessage } from './stockAffairsApi';

/**
 * 管理公司工作區及寫入狀態；查詢取消與版本檢查避免跨公司或過期回應混用。
 */
export function useStockAffairsWorkspace() {
  // 公司與主清單：同公司刷新失敗保留前次結果，切換公司立即清除。
  const companies = shallowRef<StockCompanyResponse[]>([]);
  const companyCode = ref('');
  const company = computed(() =>
    companies.value.find((item) => item.companyCode === companyCode.value),
  );
  const register = shallowRef<StockRegisterResponse | null>(null);
  const transfers = shallowRef<StockTransferResponse[]>([]);
  const meetings = shallowRef<StockMeetingResponse[]>([]);
  const dividends = shallowRef<StockDividendResponse[]>([]);
  const loading = ref(false);
  const loadError = ref('');

  // 明細預覽與寫入各自顯示錯誤，成功寫入不會被後續刷新失敗誤報為失敗。
  const notices = shallowRef<StockNoticesResponse | null>(null);
  const dividendPreview = shallowRef<StockDividendPreview | null>(null);
  const detailLoading = ref(false);
  const detailError = ref('');
  const detailKind = ref<'notice' | 'dividend' | null>(null);
  const writing = ref(false);
  const actionError = ref('');
  const actionNotice = ref('');
  let active = true;
  let loadVersion = 0;
  let detailVersion = 0;
  let loadController: AbortController | undefined;
  let detailController: AbortController | undefined;

  /**
   * 取得公司及四個工作區的同次載入結果；失敗時保留同公司的既有資料。
   */
  async function reload(): Promise<boolean> {
    if (!active) return false;
    const code = companyCode.value;
    const version = ++loadVersion;
    loadController?.abort();
    const controller = new AbortController();
    loadController = controller;
    loading.value = true;
    loadError.value = '';
    try {
      const companyRows = await stockAffairsApi.companies(controller.signal);
      if (!active || version !== loadVersion) return false;
      companies.value = companyRows;
      if (!code) {
        companyCode.value = companyRows[0]?.companyCode ?? '';
        return true;
      }
      const [registerRows, transferRows, meetingRows, dividendRows] = await Promise.all([
        stockAffairsApi.register(code, controller.signal),
        stockAffairsApi.transfers(code, controller.signal),
        stockAffairsApi.meetings(code, controller.signal),
        stockAffairsApi.dividends(code, controller.signal),
      ]);
      if (!active || version !== loadVersion || code !== companyCode.value) return false;
      register.value = registerRows;
      transfers.value = transferRows;
      meetings.value = meetingRows;
      dividends.value = dividendRows;
      return true;
    } catch (error) {
      if (active && version === loadVersion && !axios.isCancel(error)) {
        loadError.value = stockErrorMessage(error, '無法載入股務資料，請確認 API 連線後重試。');
      }
      return false;
    } finally {
      if (active && version === loadVersion) loading.value = false;
    }
  }

  /**
   * 預覽通知或股利時取消前次明細，舊請求不能覆蓋新選擇。
   */
  async function loadDetail(kind: 'notice' | 'dividend', id: string) {
    if (!active || !companyCode.value || writing.value) return;
    const code = companyCode.value;
    const version = ++detailVersion;
    detailController?.abort();
    const controller = new AbortController();
    detailController = controller;
    detailLoading.value = true;
    detailKind.value = kind;
    detailError.value = '';
    if (kind === 'notice') notices.value = null;
    else dividendPreview.value = null;
    try {
      if (kind === 'notice') {
        const result = await stockAffairsApi.notices(code, id, controller.signal);
        if (active && version === detailVersion && code === companyCode.value)
          notices.value = result;
      } else {
        const result = await stockAffairsApi.previewDividend(code, id, controller.signal);
        if (active && version === detailVersion && code === companyCode.value)
          dividendPreview.value = result;
      }
    } catch (error) {
      if (active && version === detailVersion && !axios.isCancel(error)) {
        detailError.value = stockErrorMessage(error, '無法載入明細，請重新選擇該筆資料。');
      }
    } finally {
      if (active && version === detailVersion) detailLoading.value = false;
    }
  }

  /**
   * 開始寫入後使背景讀取失效，避免舊資料在寫入期間回填。
   */
  function beginWrite(): string | null {
    if (!active || writing.value || !companyCode.value) return null;
    writing.value = true;
    actionError.value = '';
    actionNotice.value = '';
    ++loadVersion;
    loadController?.abort();
    loading.value = false;
    ++detailVersion;
    detailController?.abort();
    detailLoading.value = false;
    return companyCode.value;
  }

  /**
   * 以相同請求識別碼保護網路結果不明的重送，成功後更新統計及名簿。
   */
  async function applyTransfers(request: StockTransferRequest): Promise<boolean> {
    const code = beginWrite();
    if (!code) return false;
    try {
      const result = await stockAffairsApi.applyTransfers(code, request);
      if (!active || code !== companyCode.value) return false;
      actionNotice.value = result.alreadyApplied
        ? `此請求已完成，共 ${result.transferCount} 筆，未重複異動。`
        : `已完成 ${result.transferCount} 筆股權異動。`;
      if (!(await reload()) && active)
        actionNotice.value += ' 清單刷新失敗，請重新整理確認最新持股。';
      return true;
    } catch (error) {
      if (active && code === companyCode.value)
        actionError.value =
          stockErrorMessage(
            error,
            '無法確認異動結果，請先重新整理異動紀錄；再次送出相同內容會沿用原請求編號。',
          ) + `\n請求編號：${request.requestId}`;
      return false;
    } finally {
      if (active) writing.value = false;
    }
  }

  /**
   * 模擬發送完成後直接呈現凍結名單，另外更新會議狀態。
   */
  async function sendNotices(meetingId: string) {
    const code = beginWrite();
    if (!code) return;
    try {
      const result = await stockAffairsApi.sendNotices(code, meetingId);
      if (!active || code !== companyCode.value) return;
      notices.value = result;
      actionNotice.value = `模擬通知已完成，共 ${result.recipients.length} 位股東。`;
      if (!(await reload()) && active) actionNotice.value += ' 會議清單刷新失敗，請重新整理。';
    } catch (error) {
      if (active && code === companyCode.value)
        actionError.value = stockErrorMessage(error, '無法確認通知結果，請先重新整理會議狀態。');
    } finally {
      if (active) writing.value = false;
    }
  }

  /**
   * 只有目前預覽允許的批次能確認，完成後鎖定預覽按鈕並刷新批次清單。
   */
  async function confirmDividend(batchId: string) {
    if (dividendPreview.value?.batchId !== batchId || !dividendPreview.value.canConfirm) return;
    const code = beginWrite();
    if (!code) return;
    try {
      const result = await stockAffairsApi.confirmDividend(code, batchId);
      if (!active || code !== companyCode.value) return;
      dividendPreview.value = { ...dividendPreview.value!, canConfirm: false };
      actionNotice.value = result.alreadyCompleted
        ? `批次已完成，共 ${result.detailCount} 筆明細，未重複發放。`
        : `模擬發放已完成，共 ${result.detailCount} 筆明細。`;
      if (!(await reload()) && active) actionNotice.value += ' 批次清單刷新失敗，請重新整理。';
    } catch (error) {
      if (active && code === companyCode.value)
        actionError.value = stockErrorMessage(error, '無法確認發放結果，請先重新整理批次狀態。');
    } finally {
      if (active) writing.value = false;
    }
  }

  // 公司切換清空上一公司的資料及所有預覽；寫入時由 UI 鎖住公司選擇。
  watch(companyCode, () => {
    register.value = null;
    transfers.value = [];
    meetings.value = [];
    dividends.value = [];
    notices.value = null;
    dividendPreview.value = null;
    detailError.value = '';
    actionError.value = '';
    actionNotice.value = '';
    ++detailVersion;
    detailController?.abort();
    detailLoading.value = false;
    void reload();
  });
  onMounted(() => void reload());
  onScopeDispose(() => {
    active = false;
    loadController?.abort();
    detailController?.abort();
  });

  return {
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
    previewNotices: (id: string) => loadDetail('notice', id),
    previewDividend: (id: string) => loadDetail('dividend', id),
  };
}
