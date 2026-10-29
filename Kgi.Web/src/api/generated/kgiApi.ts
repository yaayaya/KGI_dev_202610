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
  StockCompanyResponse,
  StockDividendPreview,
  StockDividendResponse,
  StockDividendResult,
  StockMeetingResponse,
  StockNoticesResponse,
  StockRegisterResponse,
  StockTransferRequest,
  StockTransferResponse,
  StockTransferResult,
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
 * @summary 取得公司切換清單及全體名簿統計。
 */
export const getStockCompanies = (
  options?: SecondParameter<typeof request<StockCompanyResponse[]>>,
) => {
  return request<StockCompanyResponse[]>(
    { url: `/api/stock-affairs/companies`, method: 'GET' },
    options,
  );
};

/**
 * @summary 取得公司名簿與受讓人候選股東，身分資訊已遮罩。
 */
export const getStockRegister = (
  companyCode: string,
  options?: SecondParameter<typeof request<StockRegisterResponse>>,
) => {
  return request<StockRegisterResponse>(
    { url: `/api/stock-affairs/${companyCode}/register`, method: 'GET' },
    options,
  );
};

/**
 * @summary 取得公司的股權異動與稽核前後值。
 */
export const getStockTransfers = (
  companyCode: string,
  options?: SecondParameter<typeof request<StockTransferResponse[]>>,
) => {
  return request<StockTransferResponse[]>(
    { url: `/api/stock-affairs/${companyCode}/transfers`, method: 'GET' },
    options,
  );
};

/**
 * @summary 以同一交易執行單筆或批次過戶及質權異動。
 */
export const applyStockTransfers = (
  companyCode: string,
  stockTransferRequest: StockTransferRequest,
  options?: SecondParameter<typeof request<StockTransferResult>>,
) => {
  return request<StockTransferResult>(
    {
      url: `/api/stock-affairs/${companyCode}/transfers`,
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      data: stockTransferRequest,
    },
    options,
  );
};

/**
 * @summary 取得公司股東會與通知狀態。
 */
export const getStockMeetings = (
  companyCode: string,
  options?: SecondParameter<typeof request<StockMeetingResponse[]>>,
) => {
  return request<StockMeetingResponse[]>(
    { url: `/api/stock-affairs/${companyCode}/meetings`, method: 'GET' },
    options,
  );
};

/**
 * @summary 取得固定基準日通知預覽或已寄發的凍結明細。
 */
export const getStockNotices = (
  companyCode: string,
  meetingId: string,
  options?: SecondParameter<typeof request<StockNoticesResponse>>,
) => {
  return request<StockNoticesResponse>(
    { url: `/api/stock-affairs/${companyCode}/meetings/${meetingId}/notices`, method: 'GET' },
    options,
  );
};

/**
 * @summary 模擬寄發通知，重送不重複產生或更新寄發時間。
 */
export const sendStockNotices = (
  companyCode: string,
  meetingId: string,
  options?: SecondParameter<typeof request<StockNoticesResponse>>,
) => {
  return request<StockNoticesResponse>(
    { url: `/api/stock-affairs/${companyCode}/meetings/${meetingId}/notices`, method: 'POST' },
    options,
  );
};

/**
 * @summary 取得現金及股票股利批次列表。
 */
export const getStockDividends = (
  companyCode: string,
  options?: SecondParameter<typeof request<StockDividendResponse[]>>,
) => {
  return request<StockDividendResponse[]>(
    { url: `/api/stock-affairs/${companyCode}/dividends`, method: 'GET' },
    options,
  );
};

/**
 * @summary 逐戶試算股利與精確對帳，不改變批次狀態。
 */
export const previewStockDividend = (
  companyCode: string,
  batchId: string,
  options?: SecondParameter<typeof request<StockDividendPreview>>,
) => {
  return request<StockDividendPreview>(
    { url: `/api/stock-affairs/${companyCode}/dividends/${batchId}/preview`, method: 'POST' },
    options,
  );
};

/**
 * @summary 交易內重算及確認模擬發放，同批次不可重複執行。
 */
export const confirmStockDividend = (
  companyCode: string,
  batchId: string,
  options?: SecondParameter<typeof request<StockDividendResult>>,
) => {
  return request<StockDividendResult>(
    { url: `/api/stock-affairs/${companyCode}/dividends/${batchId}/confirm`, method: 'POST' },
    options,
  );
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

export type GetWarrantsResult = NonNullable<Awaited<ReturnType<typeof getWarrants>>>;
export type PreviewWarrantResult = NonNullable<Awaited<ReturnType<typeof previewWarrant>>>;
export type SaveWarrantTrialResult = NonNullable<Awaited<ReturnType<typeof saveWarrantTrial>>>;
export type GetWarrantTrialsResult = NonNullable<Awaited<ReturnType<typeof getWarrantTrials>>>;
export type GetStockCompaniesResult = NonNullable<Awaited<ReturnType<typeof getStockCompanies>>>;
export type GetStockRegisterResult = NonNullable<Awaited<ReturnType<typeof getStockRegister>>>;
export type GetStockTransfersResult = NonNullable<Awaited<ReturnType<typeof getStockTransfers>>>;
export type ApplyStockTransfersResult = NonNullable<
  Awaited<ReturnType<typeof applyStockTransfers>>
>;
export type GetStockMeetingsResult = NonNullable<Awaited<ReturnType<typeof getStockMeetings>>>;
export type GetStockNoticesResult = NonNullable<Awaited<ReturnType<typeof getStockNotices>>>;
export type SendStockNoticesResult = NonNullable<Awaited<ReturnType<typeof sendStockNotices>>>;
export type GetStockDividendsResult = NonNullable<Awaited<ReturnType<typeof getStockDividends>>>;
export type PreviewStockDividendResult = NonNullable<
  Awaited<ReturnType<typeof previewStockDividend>>
>;
export type ConfirmStockDividendResult = NonNullable<
  Awaited<ReturnType<typeof confirmStockDividend>>
>;
export type GetLendingMonitorResult = NonNullable<Awaited<ReturnType<typeof getLendingMonitor>>>;
export type ImportMarketPricesResult = NonNullable<Awaited<ReturnType<typeof importMarketPrices>>>;
