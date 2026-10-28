/**
 * 由 Orval 自動產生，請勿手動修改。
 * 來源：Kgi.Api/openapi/Kgi.Api.json
 */
export type IFormFile = Blob;

/**
 * 監控狀態：Normal 正常、Warning 預警、Call 處置、Unpriced 尚無價格。
 */
export type LendingContractResponseStatus =
  (typeof LendingContractResponseStatus)[keyof typeof LendingContractResponseStatus];

export const LendingContractResponseStatus = {
  Normal: 'Normal',
  Warning: 'Warning',
  Call: 'Call',
  Unpriced: 'Unpriced',
} as const;

/**
 * 單筆借券合約及 SQL 計算結果，金融數值以十進位字串傳輸。
 */
export interface LendingContractResponse {
  /** 借券合約編號，最多二十個字元。 */
  contractNo: string;
  /** 借出股票代碼，最多十個字元，保留前導零。 */
  stockId: string;
  /** 借出數量（張），一張為一千股，為正整數。 */
  borrowedQty: number;
  /** 擔保品總市值（元），以固定兩位小數的十進位字串表示。 */
  collateralValue: string;
  /**
   * 所選交易日收盤價（元），以固定四位小數的十進位字串表示；缺價時為 null。
   * @nullable
   */
  closingPrice: string | null;
  /**
   * 維持率（%），以固定兩位小數的十進位字串表示；缺價時為 null。狀態以未格式化數值判斷。
   * @nullable
   */
  maintenanceRatio: string | null;
  /** 監控狀態：Normal 正常、Warning 預警、Call 處置、Unpriced 尚無價格。 */
  status: LendingContractResponseStatus;
}

/**
 * 行情整批匯入成功後的結果；完成後可重新取得監控總覽。
 */
export interface LendingImportResponse {
  /** 本次匯入的行情交易日，格式為 yyyy-MM-dd。 */
  tradeDate: string;
  /** 成功匯入的股票行情筆數，同日原有行情已由本批完整替換。 */
  importedCount: number;
}

/**
 * 借券監控同一份查詢結果的統計與合約明細。
 */
export interface LendingMonitorResponse {
  /**
   * 行情交易日，格式為 yyyy-MM-dd；尚未匯入任何行情時為 null。
   * @nullable
   */
  tradeDate: string | null;
  /** 全部借券合約件數，包含尚無價格而無法計算的合約。 */
  totalCount: number;
  /** 維持率大於或等於 140% 的正常合約件數。 */
  normalCount: number;
  /** 維持率大於或等於 120%、小於 140% 的預警合約件數。 */
  warningCount: number;
  /** 維持率小於 120% 的處置合約件數。 */
  callCount: number;
  /** 缺少指定交易日行情而尚未計算的合約件數，不計入正常件數。 */
  unpricedCount: number;
  /** 全部合約明細；無合約時為空陣列，篩選及分頁由畫面處理。 */
  contracts: LendingContractResponse[];
}

export interface ProblemDetails {
  /** @nullable */
  type?: string | null;
  /** @nullable */
  title?: string | null;
  /**
   * @nullable
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  status?: number | string | null;
  /** @nullable */
  detail?: string | null;
  /** @nullable */
  instance?: string | null;
}

/**
 * 權證預覽與儲存試算使用的輸入資料。
 */
export interface TrialRequest {
  /**
   * 標的股價（元），以十進位字串傳送以保留精度；必須大於零、最多四位小數，且不超過 99999999999999.9999。API 亦相容 JSON 數字輸入。
   * @pattern ^[0-9]+(?:\.[0-9]{1,4})?$
   */
  marketPrice: string;
}

/**
 * 權證試算結果，可用於即時預覽、儲存結果與歷史紀錄。
 */
export interface TrialResponse {
  /**
   * 試算紀錄流水號；預覽時為空值（null），成功儲存後為資料庫產生的正整數。
   * @nullable
   */
  logId: number | null;
  /** 本次試算所屬的權證代碼，最多十個字元，例如 03001C。 */
  warrantId: string;
  /** 試算時輸入的標的股價（元），以固定四位小數的十進位字串表示。 */
  marketPrice: string;
  /** 計算出的權證理論價值（元），以固定四位小數的十進位字串表示。 */
  theoryPrice: string;
  /** 建議避險張數（張），以固定兩位小數的十進位字串表示。 */
  hedgeQty: string;
  /**
   * 試算存檔時間，使用 UTC 的 ISO 8601 格式；預覽時為空值（null）。
   * @nullable
   */
  createdTime: string | null;
}

/**
 * 權證類型，CALL 為認購，PUT 為認售。
 */
export type WarrantResponseWarrantType =
  (typeof WarrantResponseWarrantType)[keyof typeof WarrantResponseWarrantType];

export const WarrantResponseWarrantType = {
  CALL: 'CALL',
  PUT: 'PUT',
} as const;

/**
 * 權證基本資料，價格與比例以十進位字串保留精度。
 */
export interface WarrantResponse {
  /** 權證代碼，最多十個字元，例如 03001C。 */
  warrantId: string;
  /** 履約價格（元），以固定四位小數的十進位字串表示。 */
  strikePrice: string;
  /** 行使比例，以固定四位小數的十進位字串表示；例如 0.1000 代表十換一。 */
  conversionRatio: string;
  /** 權證類型，CALL 為認購，PUT 為認售。 */
  warrantType: WarrantResponseWarrantType;
  /** 發行商持有部位的庫存張數（張），為非負整數。 */
  positionQty: number;
}

export type GetWarrantsParams = {
  keyword?: string;
};

export type GetLendingMonitorParams = {
  tradeDate?: string;
};

export type ImportMarketPricesBody = {
  file: IFormFile;
};
