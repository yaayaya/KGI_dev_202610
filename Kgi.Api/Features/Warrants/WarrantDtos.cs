using System.Text.Json.Serialization;

namespace Kgi.Api.Features.Warrants;

/// <summary>
/// 權證預覽與儲存試算使用的輸入資料。
/// </summary>
public sealed record TrialRequest
{
    /// <summary>
    /// 標的股價（元），以十進位字串傳送以保留精度，必須大於零且最多四位小數；亦相容 JSON 數字輸入。
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal MarketPrice { get; init; }
}

/// <summary>
/// 權證基本資料，價格與比例以十進位字串保留精度。
/// </summary>
public sealed record WarrantResponse
{
    /// <summary>
    /// 權證代碼，最多十個字元，例如 03001C。
    /// </summary>
    public string WarrantId { get; init; } = string.Empty;

    /// <summary>
    /// 履約價格（元），以固定四位小數的十進位字串表示。
    /// </summary>
    public string StrikePrice { get; init; } = string.Empty;

    /// <summary>
    /// 行使比例，以固定四位小數的十進位字串表示；例如 0.1000 代表十換一。
    /// </summary>
    public string ConversionRatio { get; init; } = string.Empty;

    /// <summary>
    /// 權證類型，CALL 為認購，PUT 為認售。
    /// </summary>
    public string WarrantType { get; init; } = string.Empty;

    /// <summary>
    /// 發行商持有部位的庫存張數（張），為非負整數。
    /// </summary>
    public int PositionQty { get; init; }
}

/// <summary>
/// 權證試算結果，可用於即時預覽、儲存結果與歷史紀錄。
/// </summary>
public sealed record TrialResponse
{
    /// <summary>
    /// 試算紀錄流水號；預覽時為空值（null），成功儲存後為資料庫產生的正整數。
    /// </summary>
    public int? LogId { get; init; }

    /// <summary>
    /// 本次試算所屬的權證代碼，最多十個字元，例如 03001C。
    /// </summary>
    public string WarrantId { get; init; } = string.Empty;

    /// <summary>
    /// 試算時輸入的標的股價（元），以固定四位小數的十進位字串表示。
    /// </summary>
    public string MarketPrice { get; init; } = string.Empty;

    /// <summary>
    /// 計算出的權證理論價值（元），以固定四位小數的十進位字串表示。
    /// </summary>
    public string TheoryPrice { get; init; } = string.Empty;

    /// <summary>
    /// 建議避險張數（張），以固定兩位小數的十進位字串表示。
    /// </summary>
    public string HedgeQty { get; init; } = string.Empty;

    /// <summary>
    /// 試算存檔時間，使用 UTC 的 ISO 8601 格式；預覽時為空值（null）。
    /// </summary>
    public DateTime? CreatedTime { get; init; }
}
