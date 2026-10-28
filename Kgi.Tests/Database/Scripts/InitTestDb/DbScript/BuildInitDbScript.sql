-- 在連線指定的資料庫內重建 Q1、Q2 資料表；此操作會清除既有權證、試算、合約與行情。
-- 已有資料的 KGI 僅新增 Q2 時，請改用 AddQ2Lending.sql，再執行 Preload.sql。
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 先刪除含外鍵的試算紀錄，再刪除權證基本檔。
DROP TABLE IF EXISTS dbo.Warrant_Trial_Log;
DROP TABLE IF EXISTS dbo.Warrant_Master;
DROP TABLE IF EXISTS dbo.Market_Price;
DROP TABLE IF EXISTS dbo.Lending_Contract;
GO

-- 權證基本檔，保存權證條件與發行商庫存。
CREATE TABLE dbo.Warrant_Master
(
    -- 權證代碼，作為基本檔主鍵。
    Warrant_ID VARCHAR(10) NOT NULL,
    -- 履約價格，保留四位小數。
    Strike_Price DECIMAL(18, 4) NOT NULL,
    -- 行使比例，例如 0.1 代表十單位權證對應一單位標的。
    Conversion_Ratio DECIMAL(18, 4) NOT NULL,
    -- 權證類型：CALL 為認購，PUT 為認售。
    Warrant_Type VARCHAR(4) NOT NULL,
    -- 發行商持有的庫存張數。
    Position_Qty INT NOT NULL,
    CONSTRAINT PK_Warrant_Master PRIMARY KEY (Warrant_ID),
    CONSTRAINT CK_Warrant_Master_Strike_Price CHECK (Strike_Price > 0),
    CONSTRAINT CK_Warrant_Master_Conversion_Ratio CHECK (Conversion_Ratio > 0),
    CONSTRAINT CK_Warrant_Master_Warrant_Type CHECK (Warrant_Type IN ('CALL', 'PUT')),
    CONSTRAINT CK_Warrant_Master_Position_Qty CHECK (Position_Qty >= 0)
);

-- 權證試算紀錄，保存標的股價與後端計算結果。
CREATE TABLE dbo.Warrant_Trial_Log
(
    -- 試算紀錄流水號，由資料庫自動編號。
    Log_ID INT IDENTITY(1, 1) NOT NULL,
    -- 關聯權證基本檔的權證代碼。
    Warrant_ID VARCHAR(10) NOT NULL,
    -- 試算時輸入的標的股價，保留四位小數。
    Market_Price DECIMAL(18, 4) NOT NULL,
    -- 計算所得的理論價值，保留四位小數。
    Theory_Price DECIMAL(18, 4) NOT NULL,
    -- 建議避險張數，保留兩位小數。
    Hedge_Qty DECIMAL(18, 2) NOT NULL,
    -- 試算紀錄的建立時間，以 UTC 儲存。
    Created_Time DATETIME NOT NULL
        CONSTRAINT DF_Warrant_Trial_Log_Created_Time DEFAULT (GETUTCDATE()),
    CONSTRAINT PK_Warrant_Trial_Log PRIMARY KEY (Log_ID),
    CONSTRAINT FK_Warrant_Trial_Log_Warrant_Master
        FOREIGN KEY (Warrant_ID) REFERENCES dbo.Warrant_Master (Warrant_ID),
    CONSTRAINT CK_Warrant_Trial_Log_Market_Price CHECK (Market_Price > 0),
    CONSTRAINT CK_Warrant_Trial_Log_Theory_Price CHECK (Theory_Price >= 0),
    CONSTRAINT CK_Warrant_Trial_Log_Hedge_Qty CHECK (Hedge_Qty >= 0)
);

-- 借券合約基本檔，數量以張為單位；維持率於查詢時由 SQL JOIN 行情計算。
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

-- 每日收盤行情；同交易日整批替換，保留其他交易日以供明確日期查詢。
CREATE TABLE dbo.Market_Price
(
    Stock_ID VARCHAR(10) NOT NULL,
    Trade_Date DATE NOT NULL,
    Closing_Price DECIMAL(18, 4) NOT NULL,
    CONSTRAINT PK_Market_Price PRIMARY KEY (Trade_Date, Stock_ID),
    CONSTRAINT CK_Market_Price_Closing_Price CHECK (Closing_Price > 0)
);

COMMIT TRANSACTION;
GO

-- 資料表與欄位備註
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'權證基本檔，保存權證條件與發行商庫存。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Master';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'權證代碼，作為基本檔主鍵。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Master',
    @level2type = N'COLUMN', @level2name = N'Warrant_ID';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'履約價格，保留四位小數。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Master',
    @level2type = N'COLUMN', @level2name = N'Strike_Price';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'行使比例，例如 0.1 代表十單位權證對應一單位標的。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Master',
    @level2type = N'COLUMN', @level2name = N'Conversion_Ratio';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'權證類型：CALL 為認購，PUT 為認售。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Master',
    @level2type = N'COLUMN', @level2name = N'Warrant_Type';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'發行商持有的庫存張數。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Master',
    @level2type = N'COLUMN', @level2name = N'Position_Qty';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'權證試算紀錄，保存標的股價與後端計算結果。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Trial_Log';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'試算紀錄流水號，由資料庫自動編號。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Trial_Log',
    @level2type = N'COLUMN', @level2name = N'Log_ID';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'關聯權證基本檔的權證代碼。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Trial_Log',
    @level2type = N'COLUMN', @level2name = N'Warrant_ID';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'試算時輸入的標的股價，保留四位小數。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Trial_Log',
    @level2type = N'COLUMN', @level2name = N'Market_Price';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'計算所得的理論價值，保留四位小數。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Trial_Log',
    @level2type = N'COLUMN', @level2name = N'Theory_Price';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'建議避險張數，保留兩位小數。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Trial_Log',
    @level2type = N'COLUMN', @level2name = N'Hedge_Qty';

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description', @value = N'試算紀錄的建立時間，以 UTC 儲存。',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Warrant_Trial_Log',
    @level2type = N'COLUMN', @level2name = N'Created_Time';
GO

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

GO
