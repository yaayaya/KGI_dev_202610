-- 在連線指定的資料庫內重建 Q1、Q2、Q3；此操作會清除既有權證、試算、合約、行情及結算。
-- 已有資料的 KGI 請改用對應 AddQ2Lending.sql／AddQ3Interest.sql，再執行 Preload.sql。
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 先刪除結算明細及來源，再刪除批次；其餘功能亦依外鍵相依順序清除。
DROP TABLE IF EXISTS dbo.Interest_Detail;
DROP TABLE IF EXISTS dbo.Interest_Source;
DROP TABLE IF EXISTS dbo.Interest_Batch;
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

-- Q3 結算批次；進度及失敗資訊由短交易發布，與明細交易分開。
IF OBJECT_ID(N'dbo.Interest_Batch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Interest_Batch
    (
        Batch_ID VARCHAR(20) NOT NULL,
        Expected_Amt DECIMAL(18, 4) NOT NULL,
        Process_Status INT NOT NULL CONSTRAINT DF_Interest_Batch_Process_Status DEFAULT (0),
        Progress_Rate INT NOT NULL CONSTRAINT DF_Interest_Batch_Progress_Rate DEFAULT (0),
        Started_Time DATETIME2 NULL,
        Finished_Time DATETIME2 NULL,
        Error_Message NVARCHAR(2000) NULL,
        CONSTRAINT PK_Interest_Batch PRIMARY KEY (Batch_ID),
        CONSTRAINT CK_Interest_Batch_Expected_Amt CHECK (Expected_Amt >= 0),
        CONSTRAINT CK_Interest_Batch_Process_Status CHECK (Process_Status IN (0, 1, 2, 9)),
        CONSTRAINT CK_Interest_Batch_Progress_Rate CHECK (Progress_Rate BETWEEN 0 AND 100)
    );
END;

-- 每批固定的計息來源快照；本金與年利率獨立於 Q2 擔保品資料。
IF OBJECT_ID(N'dbo.Interest_Source', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Interest_Source
    (
        Batch_ID VARCHAR(20) NOT NULL,
        Contract_No VARCHAR(20) NOT NULL,
        Principal_Amt DECIMAL(18, 4) NOT NULL,
        Annual_Rate DECIMAL(9, 8) NOT NULL,
        CONSTRAINT PK_Interest_Source PRIMARY KEY (Batch_ID, Contract_No),
        CONSTRAINT FK_Interest_Source_Interest_Batch
            FOREIGN KEY (Batch_ID) REFERENCES dbo.Interest_Batch (Batch_ID),
        CONSTRAINT CK_Interest_Source_Principal_Amt CHECK (Principal_Amt > 0),
        CONSTRAINT CK_Interest_Source_Annual_Rate CHECK (Annual_Rate > 0)
    );
END;

-- 成功結算的明細；批次及合約唯一，並對應同一批的計息來源。
IF OBJECT_ID(N'dbo.Interest_Detail', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Interest_Detail
    (
        Detail_ID INT IDENTITY(1, 1) NOT NULL,
        Batch_ID VARCHAR(20) NOT NULL,
        Contract_No VARCHAR(20) NOT NULL,
        Daily_Interest DECIMAL(18, 4) NOT NULL,
        CONSTRAINT PK_Interest_Detail PRIMARY KEY (Detail_ID),
        CONSTRAINT UQ_Interest_Detail_Batch_ID_Contract_No UNIQUE (Batch_ID, Contract_No),
        CONSTRAINT FK_Interest_Detail_Interest_Source
            FOREIGN KEY (Batch_ID, Contract_No) REFERENCES dbo.Interest_Source (Batch_ID, Contract_No),
        CONSTRAINT CK_Interest_Detail_Daily_Interest CHECK (Daily_Interest >= 0)
    );
END;

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

-- Q3 資料表與欄位用途；增量重跑保留既有備註。

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = 0
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'借券利息結算批次，保存獨立預計總額、處理狀態、可監控進度及失敗原因。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Batch'), N'Batch_ID', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'結算批次編號，最多二十碼，作為主鍵及防止重複結算的識別。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch',
        @level2type = N'COLUMN', @level2name = N'Batch_ID';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Batch'), N'Expected_Amt', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'預計利息總額（元），固定四位小數，與逐筆結算利息總和獨立校驗。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch',
        @level2type = N'COLUMN', @level2name = N'Expected_Amt';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Batch'), N'Process_Status', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'處理狀態：0 待處理、1 已受理或處理中、2 成功、9 失敗；成功批次禁止重跑。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch',
        @level2type = N'COLUMN', @level2name = N'Process_Status';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Batch'), N'Progress_Rate', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'已發布的處理進度百分比，範圍零至一百，成功提交後才設為一百。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch',
        @level2type = N'COLUMN', @level2name = N'Progress_Rate';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Batch'), N'Started_Time', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'實際開始處理的 UTC 時間；已受理但尚未開始時為空，用以識別中斷工作。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch',
        @level2type = N'COLUMN', @level2name = N'Started_Time';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Batch'), N'Finished_Time', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'結算成功或失敗的 UTC 時間；尚未結束時為空。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch',
        @level2type = N'COLUMN', @level2name = N'Finished_Time';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Batch'), N'Error_Message', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'可供監控端顯示的失敗原因；成功或尚未失敗時為空，不包含連線機密。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Batch',
        @level2type = N'COLUMN', @level2name = N'Error_Message';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Source')
        AND minor_id = 0
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'每批不可變的合成計息來源快照；本金及利率不由 Q2 擔保品反推。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Source';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Source')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Source'), N'Batch_ID', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'所屬結算批次編號，與合約編號共同作為來源主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Source',
        @level2type = N'COLUMN', @level2name = N'Batch_ID';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Source')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Source'), N'Contract_No', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'此批計息來源的合約編號，最多二十碼，使用獨立合成資料。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Source',
        @level2type = N'COLUMN', @level2name = N'Contract_No';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Source')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Source'), N'Principal_Amt', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'計息本金（元），必須大於零，固定四位小數。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Source',
        @level2type = N'COLUMN', @level2name = N'Principal_Amt';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Source')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Source'), N'Annual_Rate', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'年利率的小數值，保留八位小數；例如百分之三保存為 0.03000000。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Source',
        @level2type = N'COLUMN', @level2name = N'Annual_Rate';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Detail')
        AND minor_id = 0
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'單批結算利息明細，與成功狀態一起提交，失敗時整批回滾。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Detail';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Detail')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Detail'), N'Detail_ID', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'結算明細流水號，由資料庫自動編號。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Detail',
        @level2type = N'COLUMN', @level2name = N'Detail_ID';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Detail')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Detail'), N'Batch_ID', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'所屬結算批次編號，須對應相同批次的來源快照。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Detail',
        @level2type = N'COLUMN', @level2name = N'Batch_ID';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Detail')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Detail'), N'Contract_No', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'來源合約編號，同批合約只允許一筆結算明細。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Detail',
        @level2type = N'COLUMN', @level2name = N'Contract_No';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.Interest_Detail')
        AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Interest_Detail'), N'Daily_Interest', 'ColumnId')
        AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'單日利息（元），本金乘年利率除以三百六十五，逐筆四捨五入至四位小數。',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Interest_Detail',
        @level2type = N'COLUMN', @level2name = N'Daily_Interest';

GO
