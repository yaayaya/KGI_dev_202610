-- 已有 KGI 新增 Q3 的入口：僅補上結算資料表、索引及備註，保留 Q1、Q2 與既有結算。
-- 手動確認連線目標後執行，再執行上層 Preload.sql 補入尚未存在的合成批次。
-- 不建立資料庫、不啟動結算，也不覆寫任何既有來源或結算結果。
SET XACT_ABORT ON;
BEGIN TRANSACTION;

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

-- 工作佇列只查處理中批次，開始時間用來區別未開始與中斷工作。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Interest_Batch')
        AND name = N'IX_Interest_Batch_Process_Status_Batch_ID'
)
    CREATE INDEX IX_Interest_Batch_Process_Status_Batch_ID
        ON dbo.Interest_Batch (Process_Status, Batch_ID)
        INCLUDE (Started_Time);

-- 單批成功明細依流水號穩定列出，涵蓋查詢所需欄位。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Interest_Detail')
        AND name = N'IX_Interest_Detail_Batch_ID_Detail_ID'
)
    CREATE INDEX IX_Interest_Detail_Batch_ID_Detail_ID
        ON dbo.Interest_Detail (Batch_ID, Detail_ID)
        INCLUDE (Contract_No, Daily_Interest);

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

COMMIT TRANSACTION;
GO
