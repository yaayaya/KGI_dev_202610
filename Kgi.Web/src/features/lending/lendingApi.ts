import axios from 'axios';
import { getLendingMonitor, importMarketPrices, resetMarketPrices } from '@/api/generated/kgiApi';

/**
 * Q2 的 API 薄轉接層：沿用 Orval 型別與請求，讀取支援取消，寫入不自動重送。
 */
export const lendingApi = {
  /**
   * 省略交易日取得最新行情；成功匯入後可明確讀取該檔交易日。
   */
  getMonitor(tradeDate?: string, signal?: AbortSignal) {
    return getLendingMonitor(tradeDate ? { tradeDate } : undefined, { signal });
  },

  /**
   * 由生成函式建立 multipart 請求；僅此匯入端點放寬等待時間。
   */
  importFile(file: File) {
    return importMarketPrices({ file }, { timeout: 60_000 });
  },

  /**
   * 清除所有交易日的行情，保留借券合約與權證資料。
   */
  resetMarketPrices() {
    return resetMarketPrices();
  },
};

/**
 * 顯示後端 ProblemDetails，避免把逐列驗證原因替換成籠統的網路錯誤。
 */
export function getLendingErrorMessage(error: unknown, fallback: string): string {
  if (!axios.isAxiosError(error)) return fallback;
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
