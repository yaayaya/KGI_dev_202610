/**
 * 由 Orval 自動產生，請勿手動修改。
 * 來源：Kgi.Api/openapi/Kgi.Api.json
 */
import type {
  GetLendingMonitorParams,
  GetWarrantsParams,
  ImportMarketPricesBody,
  LendingImportResponse,
  LendingMonitorResponse,
  TrialRequest,
  TrialResponse,
  WarrantResponse,
} from './kgiApi.schemas';

import { request } from '../mutator';

type SecondParameter<T extends (...args: never) => unknown> = Parameters<T>[1];

/**
 * @summary 查詢權證基本資料，可依代碼關鍵字篩選。
 */
export const getWarrants = (
  params?: GetWarrantsParams,
  options?: SecondParameter<typeof request<WarrantResponse[]>>,
) => {
  return request<WarrantResponse[]>({ url: `/api/warrants`, method: 'GET', params }, options);
};

/**
 * @summary 預覽理論價值與避險張數，不新增紀錄。
 */
export const previewWarrant = (
  id: string,
  trialRequest: TrialRequest,
  options?: SecondParameter<typeof request<TrialResponse>>,
) => {
  return request<TrialResponse>(
    {
      url: `/api/warrants/${id}/preview`,
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      data: trialRequest,
    },
    options,
  );
};

/**
 * @summary 依標的股價重新計算並儲存試算紀錄。
 */
export const saveWarrantTrial = (
  id: string,
  trialRequest: TrialRequest,
  options?: SecondParameter<typeof request<TrialResponse>>,
) => {
  return request<TrialResponse>(
    {
      url: `/api/warrants/${id}/trials`,
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      data: trialRequest,
    },
    options,
  );
};

/**
 * @summary 取得指定權證最近十筆試算紀錄。
 */
export const getWarrantTrials = (
  id: string,
  options?: SecondParameter<typeof request<TrialResponse[]>>,
) => {
  return request<TrialResponse[]>({ url: `/api/warrants/${id}/trials`, method: 'GET' }, options);
};

/**
 * @summary 取得指定行情日的借券監控；省略日期時使用最新行情日，尚無行情的合約標為未計算。
 */
export const getLendingMonitor = (
  params?: GetLendingMonitorParams,
  options?: SecondParameter<typeof request<LendingMonitorResponse>>,
) => {
  return request<LendingMonitorResponse>(
    { url: `/api/lending/monitor`, method: 'GET', params },
    options,
  );
};

/**
 * @summary 匯入一份完整的單日 UTF-8 CSV，檔案上限 2 MiB；同日行情整批替換，格式錯誤或缺少標的時全部拒絕。
 */
export const importMarketPrices = (
  importMarketPricesBody: ImportMarketPricesBody,
  options?: SecondParameter<typeof request<LendingImportResponse>>,
) => {
  const formData = new FormData();
  formData.append(`file`, importMarketPricesBody.file);

  return request<LendingImportResponse>(
    {
      url: `/api/lending/market-prices/import`,
      method: 'POST',
      headers: { 'Content-Type': 'multipart/form-data' },
      data: formData,
    },
    options,
  );
};

/**
 * @summary 清除全部交易日的匯入行情，保留借券合約及權證資料，監控回到尚未匯入狀態。
 */
export const resetMarketPrices = (options?: SecondParameter<typeof request<void>>) => {
  return request<void>({ url: `/api/lending/market-prices`, method: 'DELETE' }, options);
};

export type GetWarrantsResult = NonNullable<Awaited<ReturnType<typeof getWarrants>>>;
export type PreviewWarrantResult = NonNullable<Awaited<ReturnType<typeof previewWarrant>>>;
export type SaveWarrantTrialResult = NonNullable<Awaited<ReturnType<typeof saveWarrantTrial>>>;
export type GetWarrantTrialsResult = NonNullable<Awaited<ReturnType<typeof getWarrantTrials>>>;
export type GetLendingMonitorResult = NonNullable<Awaited<ReturnType<typeof getLendingMonitor>>>;
export type ImportMarketPricesResult = NonNullable<Awaited<ReturnType<typeof importMarketPrices>>>;
export type ResetMarketPricesResult = NonNullable<Awaited<ReturnType<typeof resetMarketPrices>>>;
