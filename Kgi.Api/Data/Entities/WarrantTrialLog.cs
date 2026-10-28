namespace Kgi.Api.Data.Entities;

/// <summary>
/// 已儲存的權證試算紀錄，保存當次標的股價、後端計算結果與存檔時間。
/// </summary>
public sealed class WarrantTrialLog
{
    /// <summary>
    /// 試算紀錄流水號，由資料庫自動編號；建立時間相同時用來判斷紀錄先後。
    /// </summary>
    public int LogId { get; set; }

    /// <summary>
    /// 本次試算所屬的權證代碼，最多十個字元，透過外鍵關聯權證基本檔。
    /// </summary>
    public string WarrantId { get; set; } = string.Empty;

    /// <summary>
    /// 試算時輸入的標的股價（元），必須大於零，資料庫保留四位小數。
    /// </summary>
    public decimal MarketPrice { get; set; }

    /// <summary>
    /// 後端計算的權證理論價值（元），為非負值，保留四位小數。
    /// </summary>
    public decimal TheoryPrice { get; set; }

    /// <summary>
    /// 後端計算的建議避險張數（張），為非負值，保留兩位小數。
    /// </summary>
    public decimal HedgeQty { get; set; }

    /// <summary>
    /// 試算存檔時間，預設由資料庫以 UTC 產生；讀取後由 Service 標記為 UTC 再回傳。
    /// </summary>
    public DateTime CreatedTime { get; set; }
}
