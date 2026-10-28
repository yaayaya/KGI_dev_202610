-- 已有 KGI 資料庫新增 Q2 的入口：僅補資料表、索引與備註，不刪除或覆寫既有資料。
-- 在確認連線目標為 KGI 後手動執行，再執行上層 Preload.sql 補入合成合約。
-- 本檔不建立資料庫，也不自動匯入行情。
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Lending_Contract', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Lending_Contract
    (
        Contract_No VARCHAR(20) NOT NULL,
        Stock_ID VARCHAR(10) NOT NULL,
        Borrowed_Qty INT NOT NULL,
        Collateral_Val DECIMAL(18, 2) NOT NULL,
        CONSTRAINT PK_Lending_Contract PRIMARY KEY (Contract_No),
        CONSTRAINT CK_Lending_Contract_Borrowed_Qty CHECK (Borrowed_Qty > 0),
        CONSTRAINT CK_Lending_Contract_Collateral_Val CHECK (Collateral_Val >= 0)
    );
END;

IF OBJECT_ID(N'dbo.Market_Price', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Market_Price
    (
        Stock_ID VARCHAR(10) NOT NULL,
        Trade_Date DATE NOT NULL,
        Closing_Price DECIMAL(18, 4) NOT NULL,
        CONSTRAINT PK_Market_Price PRIMARY KEY (Trade_Date, Stock_ID),
        CONSTRAINT CK_Market_Price_Closing_Price CHECK (Closing_Price > 0)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Lending_Contract')
        AND name = N'IX_Lending_Contract_Stock_ID'
)
    CREATE INDEX IX_Lending_Contract_Stock_ID
        ON dbo.Lending_Contract (Stock_ID)
        INCLUDE (Borrowed_Qty, Collateral_Val);

-- Q2 資料表與欄位用途，重跑時保留既有備註。
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Lending_Contract')
        AND minor_id = 0
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'借券合約基本檔，保存借出部位與擔保品總市值。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Lending_Contract';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Lending_Contract')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Lending_Contract'), N'Contract_No', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'借券合約編號，作為合約主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Lending_Contract',
        @level2type = N'COLUMN', @level2name = N'Contract_No';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Lending_Contract')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Lending_Contract'), N'Stock_ID', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'借出標的股票代碼，以字串保存前導零。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Lending_Contract',
        @level2type = N'COLUMN', @level2name = N'Stock_ID';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Lending_Contract')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Lending_Contract'), N'Borrowed_Qty', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'借出數量，單位為張；每張一千股，必須大於零。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Lending_Contract',
        @level2type = N'COLUMN', @level2name = N'Borrowed_Qty';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Lending_Contract')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Lending_Contract'), N'Collateral_Val', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'擔保品總市值，單位為元，保留兩位小數，不得為負。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Lending_Contract',
        @level2type = N'COLUMN', @level2name = N'Collateral_Val';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Market_Price')
        AND minor_id = 0
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'外部 CSV 匯入的每日收盤行情，以交易日與股票代碼識別。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Market_Price';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Market_Price')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Market_Price'), N'Stock_ID', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'股票代碼，以字串保存前導零，同交易日不得重複。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Market_Price',
        @level2type = N'COLUMN', @level2name = N'Stock_ID';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Market_Price')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Market_Price'), N'Trade_Date', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'行情交易日期，CSV 輸入格式為 yyyyMMdd；非檔案上傳時間。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Market_Price',
        @level2type = N'COLUMN', @level2name = N'Trade_Date';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Market_Price')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Market_Price'), N'Closing_Price', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'當日收盤價格，單位為元，保留四位小數，必須大於零。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Market_Price',
        @level2type = N'COLUMN', @level2name = N'Closing_Price';

COMMIT TRANSACTION;
GO
