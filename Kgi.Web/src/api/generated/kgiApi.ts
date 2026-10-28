/**
 * 由 Orval 自動產生，請勿手動修改。
 * 來源：Kgi.Api/openapi/Kgi.Api.json
 */
import type {
  GetWarrantsParams,
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

export type GetWarrantsResult = NonNullable<Awaited<ReturnType<typeof getWarrants>>>;
export type PreviewWarrantResult = NonNullable<Awaited<ReturnType<typeof previewWarrant>>>;
export type SaveWarrantTrialResult = NonNullable<Awaited<ReturnType<typeof saveWarrantTrial>>>;
export type GetWarrantTrialsResult = NonNullable<Awaited<ReturnType<typeof getWarrantTrials>>>;
