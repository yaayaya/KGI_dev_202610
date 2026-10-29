import axios from 'axios';
import {
  getInterestBatch,
  getInterestBatches,
  startInterestSettlement,
} from '@/api/generated/kgiApi';

/**
 * Q3 的 API 薄轉接層：讀取支援取消，批次編號先編碼，啟動結算不自動重送。
 */
export const interestApi = {
  /**
   * 取得所有結算批次的最新狀態與進度。
   */
  getBatches(signal?: AbortSignal) {
    return getInterestBatches({ signal });
  },

  /**
   * 讀取指定批次的結算結果與錯誤日誌。
   */
  getBatch(batchId: string, signal?: AbortSignal) {
    return getInterestBatch(encodeURIComponent(batchId), { signal });
  },

  /**
   * 只送出啟動指令；後續進度由監控讀取，不以長時間 HTTP 請求等待結算。
   */
  startSettlement(batchId: string) {
    return startInterestSettlement(encodeURIComponent(batchId));
  },
};

/**
 * 保留後端 ProblemDetails；後端未啟用時明確提示，不把服務問題當成結算失敗。
 */
export function getInterestErrorMessage(error: unknown, fallback: string): string {
  if (!axios.isAxiosError(error)) return fallback;
  if (error.response?.status === 503) {
    return '利息結算服務尚未就緒，請確認後端已啟用此功能，並已套用 Q3 資料庫腳本。';
  }
  const problem: unknown = error.response?.data;
  if (problem && typeof problem === 'object') {
    if ('detail' in problem && typeof problem.detail === 'string' && problem.detail.trim()) {
      return problem.detail;
    }
    if ('title' in problem && typeof problem.title === 'string' && problem.title.trim()) {
      return problem.title;
    }
  }
  return fallback;
}
