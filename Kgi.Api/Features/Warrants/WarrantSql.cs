namespace Kgi.Api.Features.Warrants;

/// <summary>
/// 集中權證功能的參數化 SQL，以明確欄位別名對應資料模型，不依賴隱含映射或查詢轉譯。
/// </summary>
internal static class WarrantSql
{
    /// <summary>
    /// 查詢權證清單；CHARINDEX 將搜尋內容視為一般字串，不解讀 LIKE 萬用字元。
    /// </summary>
    internal const string ListWarrants = """
        SELECT Warrant_ID AS WarrantId,
               Strike_Price AS StrikePrice,
               Conversion_Ratio AS ConversionRatio,
               Warrant_Type AS WarrantType,
               Position_Qty AS PositionQty
        FROM dbo.Warrant_Master
        WHERE @Keyword IS NULL OR CHARINDEX(@Keyword, Warrant_ID) > 0
        ORDER BY Warrant_ID;
        """;

    /// <summary>
    /// 依主鍵取得單一權證，參數以資料庫 varchar 型別傳入。
    /// </summary>
    internal const string GetWarrant = """
        SELECT Warrant_ID AS WarrantId,
               Strike_Price AS StrikePrice,
               Conversion_Ratio AS ConversionRatio,
               Warrant_Type AS WarrantType,
               Position_Qty AS PositionQty
        FROM dbo.Warrant_Master
        WHERE Warrant_ID = @WarrantId;
        """;

    /// <summary>
    /// 儲存試算，沿用資料庫的 UTC 時間預設值並直接取回實際存入的完整紀錄。
    /// </summary>
    internal const string InsertTrial = """
        INSERT INTO dbo.Warrant_Trial_Log
            (Warrant_ID, Market_Price, Theory_Price, Hedge_Qty)
        OUTPUT INSERTED.Log_ID AS LogId,
               INSERTED.Warrant_ID AS WarrantId,
               INSERTED.Market_Price AS MarketPrice,
               INSERTED.Theory_Price AS TheoryPrice,
               INSERTED.Hedge_Qty AS HedgeQty,
               INSERTED.Created_Time AS CreatedTime
        VALUES (@WarrantId, @MarketPrice, @TheoryPrice, @HedgeQty);
        """;

    /// <summary>
    /// 查詢最近十筆紀錄，建立時間相同時以流水號倒序維持穩定排序。
    /// </summary>
    internal const string RecentTrials = """
        SELECT TOP (10) Log_ID AS LogId,
               Warrant_ID AS WarrantId,
               Market_Price AS MarketPrice,
               Theory_Price AS TheoryPrice,
               Hedge_Qty AS HedgeQty,
               Created_Time AS CreatedTime
        FROM dbo.Warrant_Trial_Log
        WHERE Warrant_ID = @WarrantId
        ORDER BY Created_Time DESC, Log_ID DESC;
        """;
}
