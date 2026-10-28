import axios from 'axios';
import {
  getWarrants,
  getWarrantTrials,
  previewWarrant,
  saveWarrantTrial,
} from '@/api/generated/kgiApi';
import type { TrialResponse, WarrantResponse } from './warrantTypes';

/**
 * Q1 與 Orval 生成程式之間的薄轉接層，集中處理路徑編碼與查詢取消訊號。
 */
export const warrantApi = {
  /**
   * 載入完整清單供畫面本地篩選，避免搜尋每次輸入都發送請求。
   */
  async getWarrants(signal?: AbortSignal): Promise<WarrantResponse[]> {
    return getWarrants(undefined, { signal });
  },

  /**
   * 讀取指定權證的最近紀錄；代碼先編碼，取消訊號原樣交給共用 Axios。
   */
  async getHistory(warrantId: string, signal?: AbortSignal): Promise<TrialResponse[]> {
    return getWarrantTrials(encodeURIComponent(warrantId), { signal });
  },

  /**
   * 以原始股價字串呼叫預覽，不在前端轉換數值或重算財務公式。
   */
  async preview(
    warrantId: string,
    marketPrice: string,
    signal?: AbortSignal,
  ): Promise<TrialResponse> {
    return previewWarrant(encodeURIComponent(warrantId), { marketPrice }, { signal });
  },

  /**
   * 將股價送交後端重算並儲存；呼叫端負責防連點，不自動重送可能已成功的寫入。
   */
  async saveTrial(warrantId: string, marketPrice: string): Promise<TrialResponse> {
    return saveWarrantTrial(encodeURIComponent(warrantId), { marketPrice });
  },
};

/**
 * 優先顯示後端 ProblemDetails 的說明；無可用內容時採用操作情境的繁中提示。
 */
export function getWarrantErrorMessage(error: unknown, fallback: string): string {
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
