/**
 * 由 Orval 自動產生，請勿手動修改。
 * 來源：Kgi.Api/openapi/Kgi.Api.json
 */
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
