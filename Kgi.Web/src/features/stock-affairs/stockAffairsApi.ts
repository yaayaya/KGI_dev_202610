import axios from 'axios';
import {
  applyStockTransfers,
  confirmStockDividend,
  getStockCompanies,
  getStockDividends,
  getStockMeetings,
  getStockNotices,
  getStockRegister,
  getStockTransfers,
  previewStockDividend,
  sendStockNotices,
} from '@/api/generated/kgiApi';
import type { StockTransferRequest } from '@/api/generated/kgiApi.schemas';

/**
 * 股務 API 薄轉接層，使用產碼契約、編碼路徑及取消訊號，寫入不自動重送。
 */
export const stockAffairsApi = {
  companies: (signal?: AbortSignal) => getStockCompanies({ signal }),
  register: (company: string, signal?: AbortSignal) =>
    getStockRegister(encodeURIComponent(company), { signal }),
  transfers: (company: string, signal?: AbortSignal) =>
    getStockTransfers(encodeURIComponent(company), { signal }),
  applyTransfers: (company: string, request: StockTransferRequest) =>
    applyStockTransfers(encodeURIComponent(company), request),
  meetings: (company: string, signal?: AbortSignal) =>
    getStockMeetings(encodeURIComponent(company), { signal }),
  notices: (company: string, meeting: string, signal?: AbortSignal) =>
    getStockNotices(encodeURIComponent(company), encodeURIComponent(meeting), { signal }),
  sendNotices: (company: string, meeting: string) =>
    sendStockNotices(encodeURIComponent(company), encodeURIComponent(meeting)),
  dividends: (company: string, signal?: AbortSignal) =>
    getStockDividends(encodeURIComponent(company), { signal }),
  previewDividend: (company: string, batch: string, signal?: AbortSignal) =>
    previewStockDividend(encodeURIComponent(company), encodeURIComponent(batch), { signal }),
  confirmDividend: (company: string, batch: string) =>
    confirmStockDividend(encodeURIComponent(company), encodeURIComponent(batch)),
};

/**
 * 保留後端的驗證與衝突說明，連線結果不明時由呼叫處提示先查詢再重送。
 */
export function stockErrorMessage(error: unknown, fallback: string): string {
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

/**
 * 精確金融字串只加千分位，不轉為浮點數或重新計算。
 */
export function formatExact(value: string): string {
  const [whole = '', fraction] = value.split('.');
  return (
    whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + (fraction === undefined ? '' : `.${fraction}`)
  );
}

const timeFormatter = new Intl.DateTimeFormat('sv-SE', {
  timeZone: 'Asia/Taipei',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});

/**
 * UTC 操作時間固定以台北時間呈現；尚未發送的通知顯示空值。
 */
export function formatStockTime(value: string | null): string {
  return value ? timeFormatter.format(new Date(value)).replaceAll('-', '/') : '—';
}
