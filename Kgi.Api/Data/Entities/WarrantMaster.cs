namespace Kgi.Api.Data.Entities;

/// <summary>
/// 權證基本檔，保存理論價值與避險張數計算所需的權證條件及發行商庫存。
/// </summary>
public sealed class WarrantMaster
{
    /// <summary>
    /// 權證代碼，最多十個字元，作為主鍵並供試算紀錄關聯。
    /// </summary>
    public string WarrantId { get; set; } = string.Empty;

    /// <summary>
    /// 履約價格（元），必須大於零，資料庫保留四位小數。
    /// </summary>
    public decimal StrikePrice { get; set; }

    /// <summary>
    /// 行使比例，必須大於零，資料庫保留四位小數；例如 0.1 代表十單位權證對應一單位標的。
    /// </summary>
    public decimal ConversionRatio { get; set; }

    /// <summary>
    /// 權證類型，CALL 為認購，PUT 為認售，用於判斷價內、價平或價外。
    /// </summary>
    public string WarrantType { get; set; } = string.Empty;

    /// <summary>
    /// 發行商持有部位的庫存張數（張），為非負整數，用於計算建議避險張數。
    /// </summary>
    public int PositionQty { get; set; }
}
