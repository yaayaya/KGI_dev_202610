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
 * 可作受讓人的股東主檔。
 */
export interface StockCandidateResponse {
  /** 股東代碼。 */
  shareholderId: string;
  /** 股東姓名。 */
  name: string;
}

/**
 * 公司名簿總覽，聚合股數以字串保留精度。
 */
export interface StockCompanyResponse {
  /** 公司代碼。 */
  companyCode: string;
  /** 公司名稱。 */
  companyName: string;
  /** 正持股股東人數。 */
  shareholderCount: number;
  /** 總持股股數，整數字串。 */
  totalShares: string;
  /** 已質押股數，整數字串。 */
  pledgedShares: string;
  /** 已質押比例，百分比固定兩位小數字串。 */
  pledgedRatio: string;
}

/**
 * 逐戶股利試算或凍結明細。
 */
export interface StockDividendDetailResponse {
  /** 股東代碼。 */
  shareholderId: string;
  /** 基準日股東姓名。 */
  name: string;
  /** 基準日持股股數。 */
  shareQty: number;
  /** 配發金額或股數，兩位小數字串。 */
  dividendValue: string;
}

/**
 * CASH 現金或 STOCK 股票。
 */
export type StockDividendPreviewDividendType =
  (typeof StockDividendPreviewDividendType)[keyof typeof StockDividendPreviewDividendType];

export const StockDividendPreviewDividendType = {
  CASH: 'CASH',
  STOCK: 'STOCK',
} as const;

/**
 * TWD 為元，SHARE 為股。
 */
export type StockDividendPreviewUnit =
  (typeof StockDividendPreviewUnit)[keyof typeof StockDividendPreviewUnit];

export const StockDividendPreviewUnit = {
  TWD: 'TWD',
  SHARE: 'SHARE',
} as const;

/**
 * 逐戶股利試算與精確對帳結果。
 */
export interface StockDividendPreview {
  /** 批次代碼。 */
  batchId: string;
  /** CASH 現金或 STOCK 股票。 */
  dividendType: StockDividendPreviewDividendType;
  /** 每股金額或配股率，四位小數。 */
  dividendPerShare: string;
  /** 主檔預計總額，兩位小數。 */
  totalAmount: string;
  /** 逐戶加總結果，兩位小數。 */
  calculatedTotal: string;
  /** TWD 為元，SHARE 為股。 */
  unit: StockDividendPreviewUnit;
  /** 逐戶總額是否精確等於主檔。 */
  matchesExpected: boolean;
  /** 是否尚未完成且所有驗證皆通過。 */
  canConfirm: boolean;
  /**
   * 阻擋確認的原因；可確認時為 null。
   * @nullable
   */
  validationMessage: string | null;
  /** 依股東代碼排序的明細。 */
  details: StockDividendDetailResponse[];
}

/**
 * CASH 現金或 STOCK 股票。
 */
export type StockDividendResponseDividendType =
  (typeof StockDividendResponseDividendType)[keyof typeof StockDividendResponseDividendType];

export const StockDividendResponseDividendType = {
  CASH: 'CASH',
  STOCK: 'STOCK',
} as const;

/**
 * TWD 為元，SHARE 為股。
 */
export type StockDividendResponseUnit =
  (typeof StockDividendResponseUnit)[keyof typeof StockDividendResponseUnit];

export const StockDividendResponseUnit = {
  TWD: 'TWD',
  SHARE: 'SHARE',
} as const;

/**
 * 股利批次清單。
 */
export interface StockDividendResponse {
  /** 批次代碼。 */
  batchId: string;
  /** CASH 現金或 STOCK 股票。 */
  dividendType: StockDividendResponseDividendType;
  /** 每股配發金額或配股率，四位小數字串。 */
  dividendPerShare: string;
  /** 主檔預計總元或總股數，兩位小數字串。 */
  totalAmount: string;
  /** TWD 為元，SHARE 為股。 */
  unit: StockDividendResponseUnit;
  /** 0 待處理，1 完成。 */
  status: number;
  /** 基準日，yyyy-MM-dd；缺快照時為空字串。 */
  recordDate: string;
}

/**
 * 股利確認結果。
 */
export interface StockDividendResult {
  /** 股利批次代碼。 */
  batchId: string;
  /** 完成後固定為 1。 */
  status: number;
  /** 完成總額，兩位小數字串。 */
  totalAmount: string;
  /** 凍結股利明細筆數。 */
  detailCount: number;
  /** 是否回傳既有完成結果。 */
  alreadyCompleted: boolean;
}

/**
 * 會議類型 AGM 或 EGM。
 */
export type StockMeetingResponseMeetingType =
  (typeof StockMeetingResponseMeetingType)[keyof typeof StockMeetingResponseMeetingType];

export const StockMeetingResponseMeetingType = {
  AGM: 'AGM',
  EGM: 'EGM',
} as const;

/**
 * 股東會列表與通知狀態。
 */
export interface StockMeetingResponse {
  /** 會議代碼。 */
  meetingId: string;
  /** 會議類型 AGM 或 EGM。 */
  meetingType: StockMeetingResponseMeetingType;
  /** 會議日期，yyyy-MM-dd。 */
  meetingDate: string;
  /** 會議前三十日的示範基準日，yyyy-MM-dd。 */
  recordDate: string;
  /** 是否完成模擬通知。 */
  noticeSent: boolean;
  /** 正持股收件股東人數。 */
  recipientCount: number;
}

/**
 * 基準日通知收件資料。
 */
export interface StockNoticeResponse {
  /** 股東代碼。 */
  shareholderId: string;
  /** 基準日股東姓名。 */
  name: string;
  /** 基準日通知地址。 */
  address: string;
  /** 基準日持股股數。 */
  shareQty: number;
  /**
   * 模擬通知時間，UTC；未寄發為 null。
   * @nullable
   */
  sentTime: string | null;
}

/**
 * 股東會通知預覽或已寄發明細。
 */
export interface StockNoticesResponse {
  /** 會議代碼。 */
  meetingId: string;
  /** 是否已完成模擬通知。 */
  noticeSent: boolean;
  /** 固定基準日的收件名單。 */
  recipients: StockNoticeResponse[];
}

/**
 * 目前公司股東名簿，身分欄位僅提供遮罩。
 */
export interface StockShareholderResponse {
  /** 股東代碼。 */
  shareholderId: string;
  /** 股東姓名。 */
  name: string;
  /** 已遮罩的測試身分證或統編。 */
  maskedIdNo: string;
  /** 通訊地址。 */
  address: string;
  /** 持股股數，非負整數。 */
  shareQty: number;
  /** 質押股數，非負整數。 */
  pledgeQty: number;
  /** 可用股數，持股減質押。 */
  availableQty: number;
}

/**
 * 指定公司的名簿與全體股東候選清單。
 */
export interface StockRegisterResponse {
  /** 公司代碼。 */
  companyCode: string;
  /** 目前名簿，包含零持股者。 */
  shareholders: StockShareholderResponse[];
  /** 可供受讓的股東主檔。 */
  candidates: StockCandidateResponse[];
}

/**
 * 異動類型，TRANSFER、PLEDGE、UNPLEDGE。
 */
export type StockTransferItemTransferType =
  (typeof StockTransferItemTransferType)[keyof typeof StockTransferItemTransferType];

export const StockTransferItemTransferType = {
  TRANSFER: 'TRANSFER',
  PLEDGE: 'PLEDGE',
  UNPLEDGE: 'UNPLEDGE',
} as const;

/**
 * 一筆過戶或質權異動。
 */
export interface StockTransferItem {
  /** 異動類型，TRANSFER、PLEDGE、UNPLEDGE。 */
  transferType: StockTransferItemTransferType;
  /** 出讓人或質權操作股東。 */
  fromShareholder: string;
  /**
   * 受讓人；質權操作時可省略或為 null。
   * @nullable
   */
  toShareholder?: string | null;
  /** 異動股數，必須為正整數。 */
  quantity: number;
}

/**
 * 單筆或最多一百筆股權異動請求。
 */
export interface StockTransferRequest {
  /** 防重送識別碼，一至六十四字元。 */
  requestId: string;
  /** 依輸入順序處理的異動明細。 */
  items: StockTransferItem[];
}

/**
 * 股權異動與前後值稽核紀錄。
 */
export interface StockTransferResponse {
  /** 異動流水號。 */
  transferId: number;
  /** 異動請求識別碼。 */
  requestId: string;
  /** 異動類型。 */
  transferType: string;
  /** 出讓人或質權股東。 */
  fromShareholder: string;
  /** 受讓人；質權時為同一股東。 */
  toShareholder: string;
  /** 異動股數。 */
  transferQty: number;
  /** 出讓人異動前持股。 */
  fromShareQtyBefore: number;
  /** 出讓人異動後持股。 */
  fromShareQtyAfter: number;
  /** 出讓人異動前質押。 */
  fromPledgeQtyBefore: number;
  /** 出讓人異動後質押。 */
  fromPledgeQtyAfter: number;
  /** 受讓人異動前持股。 */
  toShareQtyBefore: number;
  /** 受讓人異動後持股。 */
  toShareQtyAfter: number;
  /** 受讓人異動前質押。 */
  toPledgeQtyBefore: number;
  /** 受讓人異動後質押。 */
  toPledgeQtyAfter: number;
  /** 後端記錄的示範操作者。 */
  operatorId: string;
  /** 操作時間，ISO 8601 UTC。 */
  createdTime: string;
}

/**
 * 異動交易執行結果。
 */
export interface StockTransferResult {
  /** 本次請求識別碼。 */
  requestId: string;
  /** 成功的異動明細筆數。 */
  transferCount: number;
  /** 是否為相同請求的既有結果。 */
  alreadyApplied: boolean;
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
