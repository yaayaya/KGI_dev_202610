namespace Kgi.Api.Features.Lending;

/// <summary>
/// 借券行情匯入與維持率監控的參數化 SQL，整批 JOIN 計算，避免逐筆查詢合約。
/// </summary>
public static class LendingSql
{
    public const string Monitor = """
        DECLARE @EffectiveDate DATE = COALESCE(@TradeDate, (SELECT MAX(Trade_Date) FROM dbo.Market_Price));
        SELECT @EffectiveDate;

        SELECT C.Contract_No AS ContractNo,
               C.Stock_ID AS StockId,
               C.Borrowed_Qty AS BorrowedQty,
               C.Collateral_Val AS CollateralValue,
               P.Closing_Price AS ClosingPrice,
               CAST((CAST((N.Numerator - R.Remainder) / NULLIF(E.Exposure, 0) AS DECIMAL(28, 0))
                   + CASE WHEN R.Remainder * CAST(2 AS DECIMAL(1, 0)) >= E.Exposure THEN 1 ELSE 0 END)
                   / CAST(100 AS DECIMAL(3, 0)) AS DECIMAL(28, 2)) AS MaintenanceRatio,
               CASE WHEN P.Stock_ID IS NULL THEN 'Unpriced'
                    WHEN C.Collateral_Val * CAST(100 AS DECIMAL(3, 0)) < E.Exposure * CAST(120 AS DECIMAL(3, 0)) THEN 'Call'
                    WHEN C.Collateral_Val * CAST(100 AS DECIMAL(3, 0)) < E.Exposure * CAST(140 AS DECIMAL(3, 0)) THEN 'Warning'
                    ELSE 'Normal' END AS Status
        FROM dbo.Lending_Contract AS C
        LEFT JOIN dbo.Market_Price AS P ON P.Stock_ID = C.Stock_ID AND P.Trade_Date = @EffectiveDate
        CROSS APPLY
        (
            -- 先轉 decimal 再乘一千股；最大乘積使用 34,4，門檻乘積最多 38,4，不因 scale 縮減誤分類。
            SELECT CAST(CAST(C.Borrowed_Qty AS DECIMAL(10, 0)) * CAST(1000 AS DECIMAL(4, 0))
                   * P.Closing_Price AS DECIMAL(34, 4)) AS Exposure
        ) AS E
        CROSS APPLY
        (
            -- 百分比的百分之一為一單位，先取整數商與餘數，只在半分界捨入一次，避免兩次捨入進位。
            SELECT C.Collateral_Val * CAST(10000 AS DECIMAL(5, 0)) AS Numerator
        ) AS N
        CROSS APPLY
        (
            SELECT N.Numerator % NULLIF(E.Exposure, 0) AS Remainder
        ) AS R
        ORDER BY CASE WHEN P.Stock_ID IS NULL THEN 3
                      WHEN C.Collateral_Val * CAST(100 AS DECIMAL(3, 0)) < E.Exposure * CAST(120 AS DECIMAL(3, 0)) THEN 0
                      WHEN C.Collateral_Val * CAST(100 AS DECIMAL(3, 0)) < E.Exposure * CAST(140 AS DECIMAL(3, 0)) THEN 1
                      ELSE 2 END,
                 C.Contract_No;
        """;

    public const string AcquireImportLock = """
        DECLARE @LockResult INT;
        EXEC @LockResult = sys.sp_getapplock @Resource = @LockResource,
            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        SELECT @LockResult;
        """;

    public const string MissingStocks = """
        SELECT DISTINCT C.Stock_ID
        FROM dbo.Lending_Contract AS C
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM OPENJSON(@Prices)
            WITH (StockId VARCHAR(10) '$.stockId') AS P
            WHERE P.StockId = C.Stock_ID
        )
        ORDER BY C.Stock_ID;
        """;

    public const string ReplacePrices = """
        DELETE FROM dbo.Market_Price WHERE Trade_Date = @TradeDate;
        INSERT INTO dbo.Market_Price (Stock_ID, Closing_Price, Trade_Date)
        SELECT P.StockId, P.ClosingPrice, @TradeDate
        FROM OPENJSON(@Prices)
        WITH
        (
            StockId VARCHAR(10) '$.stockId',
            ClosingPrice DECIMAL(18, 4) '$.closingPrice'
        ) AS P;
        """;
}
