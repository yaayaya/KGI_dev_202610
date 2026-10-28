SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 合成測試資料，不代表真實掛牌權證、即時行情或券商庫存。
-- 固定 SHA-256 種子讓同一代碼的參數可重現；沿用原代碼以維持試算紀錄關聯。
-- 03001C、03002P 保留履約價 100、行使比例 0.1、庫存 1000 張的教學範例。
DECLARE @SeedWarrants TABLE
(
    Warrant_ID VARCHAR(10) NOT NULL PRIMARY KEY,
    Strike_Price DECIMAL(18, 4) NOT NULL,
    Conversion_Ratio DECIMAL(18, 4) NOT NULL,
    Warrant_Type VARCHAR(4) NOT NULL,
    Position_Qty INT NOT NULL
);

;WITH Digits AS
(
    SELECT Digit
    FROM (VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)) AS ValuesTable(Digit)
),
Numbers AS
(
    SELECT 1 + Ones.Digit + Tens.Digit * 10 + Hundreds.Digit * 100 AS Number
    FROM Digits AS Ones
    CROSS JOIN Digits AS Tens
    CROSS JOIN Digits AS Hundreds
    WHERE Hundreds.Digit < 8
),
RandomValues AS
(
    SELECT Number,
        -- 各段雜湊分別控制價格帶、參考價、價內外程度、比例與庫存，避免欄位同步循環。
        CONVERT(BIGINT, SUBSTRING(SeedHash, 1, 4)) AS BandRandom,
        CONVERT(BIGINT, SUBSTRING(SeedHash, 5, 4)) AS PriceRandom,
        CONVERT(BIGINT, SUBSTRING(SeedHash, 9, 4)) AS StrikeRandom,
        CONVERT(BIGINT, SUBSTRING(SeedHash, 13, 4)) AS RatioRandom,
        CONVERT(BIGINT, SUBSTRING(SeedHash, 17, 4)) AS PositionBandRandom,
        CONVERT(BIGINT, SUBSTRING(SeedHash, 21, 4)) AS PositionRandom
    FROM Numbers
    CROSS APPLY (VALUES (HASHBYTES('SHA2_256', CONCAT('KGI-Q1-SEED-v2|', Number)))) AS Hashes(SeedHash)
),
MarketProfiles AS
(
    SELECT *,
        -- 低、中、高價情境採不同權重；分布為測試設計，並非市場統計。
        CASE
            WHEN BandRandom % 100 < 20 THEN 0
            WHEN BandRandom % 100 < 50 THEN 1
            WHEN BandRandom % 100 < 75 THEN 2
            WHEN BandRandom % 100 < 95 THEN 3
            ELSE 4
        END AS PriceBand
    FROM RandomValues
),
ReferencePrices AS
(
    SELECT *,
        CONVERT(DECIMAL(18, 4), CASE PriceBand
            WHEN 0 THEN 10 + (PriceRandom % 4000) / 100.0
            WHEN 1 THEN 50 + (PriceRandom % 15000) / 100.0
            WHEN 2 THEN 200 + (PriceRandom % 30000) / 100.0
            WHEN 3 THEN 500 + (PriceRandom % 100000) / 100.0
            ELSE 1500 + (PriceRandom % 200000) / 100.0
        END) AS ReferencePrice
    FROM MarketProfiles
)
INSERT INTO @SeedWarrants
    (Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty)
SELECT
    RIGHT('00000' + CONVERT(VARCHAR(5), 3000 + Number), 5)
        + CASE WHEN Number % 2 = 1 THEN 'C' ELSE 'P' END,
    CONVERT(DECIMAL(18, 4), CASE WHEN Number <= 2 THEN 100
        -- 履約價落於合成參考價的 80%～130%，包含四位小數的調整情境。
        ELSE ROUND(ReferencePrice * (8000 + StrikeRandom % 5001) / 10000.0, 4)
    END),
    CONVERT(DECIMAL(18, 4), CASE WHEN Number <= 2 THEN 0.1 ELSE
        -- 高價情境配置較小行使比例；保留非整齊小數以涵蓋精度與取位操作。
        CASE PriceBand
            WHEN 0 THEN CASE RatioRandom % 6
                WHEN 0 THEN 0.0500 WHEN 1 THEN 0.1000 WHEN 2 THEN 0.2000
                WHEN 3 THEN 0.2500 WHEN 4 THEN 0.5000 ELSE 1.0000 END
            WHEN 1 THEN CASE RatioRandom % 6
                WHEN 0 THEN 0.0100 WHEN 1 THEN 0.0250 WHEN 2 THEN 0.0500
                WHEN 3 THEN 0.0750 WHEN 4 THEN 0.1000 ELSE 0.2000 END
            WHEN 2 THEN CASE RatioRandom % 6
                WHEN 0 THEN 0.0050 WHEN 1 THEN 0.0100 WHEN 2 THEN 0.0125
                WHEN 3 THEN 0.0250 WHEN 4 THEN 0.0500 ELSE 0.1000 END
            WHEN 3 THEN CASE RatioRandom % 6
                WHEN 0 THEN 0.0010 WHEN 1 THEN 0.0025 WHEN 2 THEN 0.0050
                WHEN 3 THEN 0.0075 WHEN 4 THEN 0.0100 ELSE 0.0250 END
            ELSE CASE RatioRandom % 6
                WHEN 0 THEN 0.0005 WHEN 1 THEN 0.0010 WHEN 2 THEN 0.0025
                WHEN 3 THEN 0.0050 WHEN 4 THEN 0.0100 ELSE 0.0200 END
        END
    END),
    CASE WHEN Number % 2 = 1 THEN 'CALL' ELSE 'PUT' END,
    CASE
        WHEN Number <= 2 THEN 1000
        WHEN PositionBandRandom % 20 = 0 THEN 0
        WHEN PositionBandRandom % 20 < 4 THEN 1 + CONVERT(INT, PositionRandom % 100)
        WHEN PositionBandRandom % 20 < 12 THEN 100 + CONVERT(INT, PositionRandom % 4901)
        ELSE 5001 + CONVERT(INT, PositionRandom % 45000)
    END
FROM ReferencePrices;

-- 初始化僅補缺少的代碼，不覆寫既有部位，也不刪除或重算試算紀錄。
INSERT INTO dbo.Warrant_Master
    (Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty)
SELECT Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty
FROM @SeedWarrants AS Seed
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Warrant_Master AS Existing
    WHERE Existing.Warrant_ID = Seed.Warrant_ID
);

COMMIT TRANSACTION;
GO
