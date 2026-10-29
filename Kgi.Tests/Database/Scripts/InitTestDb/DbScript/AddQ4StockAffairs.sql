-- 既有 KGI 加入 Q4：只建立尚未存在的資料表、索引與繁中備註，不刪除或覆寫資料。
-- 確認目標資料庫後手動執行，再執行 Preload.sql；API 啟動不會自動套用。
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 合成示範公司的基本資料，供公司切換及識別空公司。
IF OBJECT_ID(N'dbo.Company_Master', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Company_Master
    (
        Company_Code VARCHAR(10) NOT NULL,
        Company_Name NVARCHAR(50) NOT NULL,
        CONSTRAINT PK_Company_Master PRIMARY KEY (Company_Code)
    );
END;

-- 股東基本資料；身分與地址均為合成示範內容。
IF OBJECT_ID(N'dbo.Shareholder_Master', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Shareholder_Master
    (
        Shareholder_ID VARCHAR(20) NOT NULL,
        Shareholder_Name NVARCHAR(50) NOT NULL,
        ID_No VARCHAR(20) NOT NULL,
        Address NVARCHAR(100) NOT NULL,
        Created_Time DATETIME NOT NULL CONSTRAINT DF_Shareholder_Master_Created_Time DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_Shareholder_Master PRIMARY KEY (Shareholder_ID)
    );
END;

-- 目前股東名簿；保留零股數資料，異動必須與操作紀錄同交易提交。
IF OBJECT_ID(N'dbo.Shareholding_Register', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Shareholding_Register
    (
        Company_Code VARCHAR(10) NOT NULL,
        Shareholder_ID VARCHAR(20) NOT NULL,
        Share_Qty INT NOT NULL,
        Pledge_Qty INT NOT NULL,
        Last_Update_Time DATETIME NOT NULL CONSTRAINT DF_Shareholding_Register_Last_Update_Time DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_Shareholding_Register PRIMARY KEY (Company_Code, Shareholder_ID),
        CONSTRAINT FK_Shareholding_Register_Company FOREIGN KEY (Company_Code) REFERENCES dbo.Company_Master (Company_Code),
        CONSTRAINT FK_Shareholding_Register_Shareholder FOREIGN KEY (Shareholder_ID) REFERENCES dbo.Shareholder_Master (Shareholder_ID),
        CONSTRAINT CK_Shareholding_Register_Quantities CHECK (Share_Qty >= 0 AND Pledge_Qty >= 0 AND Pledge_Qty <= Share_Qty)
    );
END;

-- 公司基準日的不可變股權快照主檔；不以目前名簿回推歷史。
IF OBJECT_ID(N'dbo.Shareholding_Snapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Shareholding_Snapshot
    (
        Snapshot_ID VARCHAR(30) NOT NULL,
        Company_Code VARCHAR(10) NOT NULL,
        Record_Date DATE NOT NULL,
        Created_Time DATETIME NOT NULL CONSTRAINT DF_Shareholding_Snapshot_Created_Time DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_Shareholding_Snapshot PRIMARY KEY (Snapshot_ID),
        CONSTRAINT UQ_Shareholding_Snapshot_Company_Date UNIQUE (Company_Code, Record_Date),
        CONSTRAINT UQ_Shareholding_Snapshot_ID_Company UNIQUE (Snapshot_ID, Company_Code),
        CONSTRAINT FK_Shareholding_Snapshot_Company FOREIGN KEY (Company_Code) REFERENCES dbo.Company_Master (Company_Code)
    );
END;

-- 基準日正持股股東及當日姓名地址；通知與股利計算的不可變來源。
IF OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Shareholding_Snapshot_Detail
    (
        Snapshot_ID VARCHAR(30) NOT NULL,
        Shareholder_ID VARCHAR(20) NOT NULL,
        Shareholder_Name NVARCHAR(50) NOT NULL,
        Address NVARCHAR(100) NOT NULL,
        Share_Qty INT NOT NULL,
        Pledge_Qty INT NOT NULL,
        CONSTRAINT PK_Shareholding_Snapshot_Detail PRIMARY KEY (Snapshot_ID, Shareholder_ID),
        CONSTRAINT FK_Shareholding_Snapshot_Detail_Snapshot FOREIGN KEY (Snapshot_ID) REFERENCES dbo.Shareholding_Snapshot (Snapshot_ID),
        CONSTRAINT FK_Shareholding_Snapshot_Detail_Shareholder FOREIGN KEY (Shareholder_ID) REFERENCES dbo.Shareholder_Master (Shareholder_ID),
        CONSTRAINT CK_Shareholding_Snapshot_Detail_Quantities CHECK (Share_Qty > 0 AND Pledge_Qty >= 0 AND Pledge_Qty <= Share_Qty)
    );
END;

-- 成功股權異動請求及內容指紋，支援相同請求重送且不重複異動。
IF OBJECT_ID(N'dbo.Share_Operation_Request', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Share_Operation_Request
    (
        Request_ID VARCHAR(64) NOT NULL,
        Company_Code VARCHAR(10) NOT NULL,
        Payload_Hash CHAR(64) NOT NULL,
        Operator_ID VARCHAR(50) NOT NULL,
        Created_Time DATETIME NOT NULL CONSTRAINT DF_Share_Operation_Request_Created_Time DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_Share_Operation_Request PRIMARY KEY (Request_ID),
        CONSTRAINT UQ_Share_Operation_Request_ID_Company UNIQUE (Request_ID, Company_Code),
        CONSTRAINT FK_Share_Operation_Request_Company FOREIGN KEY (Company_Code) REFERENCES dbo.Company_Master (Company_Code)
    );
END;

-- 股票過戶與質權設定撤銷的逐項稽核紀錄，保存雙方完整異動前後值。
IF OBJECT_ID(N'dbo.Share_Transfer_Log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Share_Transfer_Log
    (
        Transfer_ID INT IDENTITY(1,1) NOT NULL,
        Company_Code VARCHAR(10) NOT NULL,
        From_Shareholder VARCHAR(20) NOT NULL,
        To_Shareholder VARCHAR(20) NOT NULL,
        Transfer_Qty INT NOT NULL,
        Transfer_Type VARCHAR(10) NOT NULL,
        Created_Time DATETIME NOT NULL CONSTRAINT DF_Share_Transfer_Log_Created_Time DEFAULT (GETUTCDATE()),
        Request_ID VARCHAR(64) NOT NULL,
        Item_No INT NOT NULL,
        Operator_ID VARCHAR(50) NOT NULL,
        From_Share_Qty_Before INT NOT NULL,
        From_Share_Qty_After INT NOT NULL,
        From_Pledge_Qty_Before INT NOT NULL,
        From_Pledge_Qty_After INT NOT NULL,
        To_Share_Qty_Before INT NOT NULL,
        To_Share_Qty_After INT NOT NULL,
        To_Pledge_Qty_Before INT NOT NULL,
        To_Pledge_Qty_After INT NOT NULL,
        CONSTRAINT PK_Share_Transfer_Log PRIMARY KEY (Transfer_ID),
        CONSTRAINT UQ_Share_Transfer_Log_Request_Item UNIQUE (Request_ID, Item_No),
        CONSTRAINT FK_Share_Transfer_Log_Request_Company FOREIGN KEY (Request_ID, Company_Code) REFERENCES dbo.Share_Operation_Request (Request_ID, Company_Code),
        CONSTRAINT FK_Share_Transfer_Log_From_Shareholder FOREIGN KEY (From_Shareholder) REFERENCES dbo.Shareholder_Master (Shareholder_ID),
        CONSTRAINT FK_Share_Transfer_Log_To_Shareholder FOREIGN KEY (To_Shareholder) REFERENCES dbo.Shareholder_Master (Shareholder_ID),
        CONSTRAINT CK_Share_Transfer_Log_Type CHECK (Transfer_Type IN ('TRANSFER', 'PLEDGE', 'UNPLEDGE')),
        CONSTRAINT CK_Share_Transfer_Log_Quantity_Item CHECK (Transfer_Qty > 0 AND Item_No BETWEEN 1 AND 100),
        CONSTRAINT CK_Share_Transfer_Log_Participants CHECK ((Transfer_Type = 'TRANSFER' AND From_Shareholder <> To_Shareholder) OR (Transfer_Type IN ('PLEDGE', 'UNPLEDGE') AND From_Shareholder = To_Shareholder)),
        CONSTRAINT CK_Share_Transfer_Log_Balances CHECK (From_Share_Qty_Before >= 0 AND From_Pledge_Qty_Before >= 0 AND From_Pledge_Qty_Before <= From_Share_Qty_Before AND From_Share_Qty_After >= 0 AND From_Pledge_Qty_After >= 0 AND From_Pledge_Qty_After <= From_Share_Qty_After AND To_Share_Qty_Before >= 0 AND To_Pledge_Qty_Before >= 0 AND To_Pledge_Qty_Before <= To_Share_Qty_Before AND To_Share_Qty_After >= 0 AND To_Pledge_Qty_After >= 0 AND To_Pledge_Qty_After <= To_Share_Qty_After)
    );
END;

-- 股東會日期、固定基準日快照與通知完成旗標。
IF OBJECT_ID(N'dbo.Shareholders_Meeting', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Shareholders_Meeting
    (
        Meeting_ID VARCHAR(20) NOT NULL,
        Company_Code VARCHAR(10) NOT NULL,
        Meeting_Type VARCHAR(10) NOT NULL,
        Meeting_Date DATE NOT NULL,
        Notice_Sent_Flag BIT NOT NULL CONSTRAINT DF_Shareholders_Meeting_Notice_Sent_Flag DEFAULT (0),
        Snapshot_ID VARCHAR(30) NULL,
        CONSTRAINT PK_Shareholders_Meeting PRIMARY KEY (Meeting_ID),
        CONSTRAINT FK_Shareholders_Meeting_Company FOREIGN KEY (Company_Code) REFERENCES dbo.Company_Master (Company_Code),
        CONSTRAINT FK_Shareholders_Meeting_Snapshot_Company FOREIGN KEY (Snapshot_ID, Company_Code) REFERENCES dbo.Shareholding_Snapshot (Snapshot_ID, Company_Code),
        CONSTRAINT CK_Shareholders_Meeting_Type CHECK (Meeting_Type IN ('AGM', 'EGM'))
    );
END;

-- 已完成模擬寄發的通知名單；複製基準日資料，重送不得改變內容。
IF OBJECT_ID(N'dbo.Meeting_Notice_Detail', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Meeting_Notice_Detail
    (
        Meeting_ID VARCHAR(20) NOT NULL,
        Shareholder_ID VARCHAR(20) NOT NULL,
        Shareholder_Name NVARCHAR(50) NOT NULL,
        Address NVARCHAR(100) NOT NULL,
        Share_Qty INT NOT NULL,
        Sent_Time DATETIME NOT NULL CONSTRAINT DF_Meeting_Notice_Detail_Sent_Time DEFAULT (GETUTCDATE()),
        Operator_ID VARCHAR(50) NOT NULL,
        CONSTRAINT PK_Meeting_Notice_Detail PRIMARY KEY (Meeting_ID, Shareholder_ID),
        CONSTRAINT FK_Meeting_Notice_Detail_Meeting FOREIGN KEY (Meeting_ID) REFERENCES dbo.Shareholders_Meeting (Meeting_ID),
        CONSTRAINT FK_Meeting_Notice_Detail_Shareholder FOREIGN KEY (Shareholder_ID) REFERENCES dbo.Shareholder_Master (Shareholder_ID),
        CONSTRAINT CK_Meeting_Notice_Detail_Share_Qty CHECK (Share_Qty > 0)
    );
END;

-- 現金或股票股利批次，精確對帳後與凍結明細同交易完成。
IF OBJECT_ID(N'dbo.Dividend_Batch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Dividend_Batch
    (
        Batch_ID VARCHAR(20) NOT NULL,
        Company_Code VARCHAR(10) NOT NULL,
        Dividend_Type VARCHAR(10) NOT NULL,
        Dividend_Per_Share DECIMAL(18,4) NOT NULL,
        Total_Amount DECIMAL(18,2) NOT NULL,
        Status INT NOT NULL CONSTRAINT DF_Dividend_Batch_Status DEFAULT (0),
        Snapshot_ID VARCHAR(30) NULL,
        Completed_Time DATETIME NULL,
        Operator_ID VARCHAR(50) NULL,
        CONSTRAINT PK_Dividend_Batch PRIMARY KEY (Batch_ID),
        CONSTRAINT FK_Dividend_Batch_Company FOREIGN KEY (Company_Code) REFERENCES dbo.Company_Master (Company_Code),
        CONSTRAINT FK_Dividend_Batch_Snapshot_Company FOREIGN KEY (Snapshot_ID, Company_Code) REFERENCES dbo.Shareholding_Snapshot (Snapshot_ID, Company_Code),
        CONSTRAINT CK_Dividend_Batch_Type CHECK (Dividend_Type IN ('CASH', 'STOCK')),
        CONSTRAINT CK_Dividend_Batch_Values CHECK (Dividend_Per_Share > 0 AND Total_Amount >= 0),
        CONSTRAINT CK_Dividend_Batch_Status CHECK (Status IN (0, 1)),
        CONSTRAINT CK_Dividend_Batch_Completion CHECK ((Status = 0 AND Completed_Time IS NULL AND Operator_ID IS NULL) OR (Status = 1 AND Completed_Time IS NOT NULL AND Operator_ID IS NOT NULL))
    );
END;

-- 已確認股利的凍結逐戶計算結果，不回寫目前持股。
IF OBJECT_ID(N'dbo.Dividend_Detail', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Dividend_Detail
    (
        Batch_ID VARCHAR(20) NOT NULL,
        Shareholder_ID VARCHAR(20) NOT NULL,
        Shareholder_Name NVARCHAR(50) NOT NULL,
        Share_Qty INT NOT NULL,
        Dividend_Per_Share DECIMAL(18,4) NOT NULL,
        Dividend_Value DECIMAL(18,2) NOT NULL,
        CONSTRAINT PK_Dividend_Detail PRIMARY KEY (Batch_ID, Shareholder_ID),
        CONSTRAINT FK_Dividend_Detail_Batch FOREIGN KEY (Batch_ID) REFERENCES dbo.Dividend_Batch (Batch_ID),
        CONSTRAINT FK_Dividend_Detail_Shareholder FOREIGN KEY (Shareholder_ID) REFERENCES dbo.Shareholder_Master (Shareholder_ID),
        CONSTRAINT CK_Dividend_Detail_Values CHECK (Share_Qty > 0 AND Dividend_Per_Share > 0 AND Dividend_Value >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND name = N'IX_Share_Transfer_Log_Company_Time_ID')
    CREATE INDEX IX_Share_Transfer_Log_Company_Time_ID
    ON dbo.Share_Transfer_Log (Company_Code, Created_Time DESC, Transfer_ID DESC)
    INCLUDE (Request_ID, Item_No, Transfer_Type, From_Shareholder, To_Shareholder, Transfer_Qty);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND name = N'IX_Shareholders_Meeting_Company_Date_ID')
    CREATE INDEX IX_Shareholders_Meeting_Company_Date_ID
    ON dbo.Shareholders_Meeting (Company_Code, Meeting_Date DESC, Meeting_ID)
    INCLUDE (Meeting_Type, Notice_Sent_Flag, Snapshot_ID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Dividend_Batch') AND name = N'IX_Dividend_Batch_Company_ID')
    CREATE INDEX IX_Dividend_Batch_Company_ID
    ON dbo.Dividend_Batch (Company_Code, Batch_ID)
    INCLUDE (Dividend_Type, Status, Snapshot_ID);

-- 資料表與每個欄位均保存繁中用途；重跑時保留既有備註。

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Company_Master') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'合成示範公司的基本資料，供公司切換及識別空公司。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Company_Master';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Company_Master') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Company_Master'), N'Company_Code', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'公司代碼，作為公司主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Company_Master', @level2type = N'COLUMN', @level2name = N'Company_Code';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Company_Master') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Company_Master'), N'Company_Name', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'公司顯示名稱。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Company_Master', @level2type = N'COLUMN', @level2name = N'Company_Name';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholder_Master') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東基本資料；身分與地址均為合成示範內容。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholder_Master';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholder_Master') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholder_Master'), N'Shareholder_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東代碼，作為股東主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholder_Master', @level2type = N'COLUMN', @level2name = N'Shareholder_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholder_Master') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholder_Master'), N'Shareholder_Name', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東姓名。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholder_Master', @level2type = N'COLUMN', @level2name = N'Shareholder_Name';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholder_Master') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholder_Master'), N'ID_No', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'合成身分識別字串；API 僅回傳遮罩內容。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholder_Master', @level2type = N'COLUMN', @level2name = N'ID_No';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholder_Master') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholder_Master'), N'Address', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東目前通訊地址。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholder_Master', @level2type = N'COLUMN', @level2name = N'Address';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholder_Master') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholder_Master'), N'Created_Time', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東建檔時間，以 UTC 儲存。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholder_Master', @level2type = N'COLUMN', @level2name = N'Created_Time';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Register') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'目前股東名簿；保留零股數資料，異動必須與操作紀錄同交易提交。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Register';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Register') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Register'), N'Company_Code', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'公司代碼，與股東代碼組成主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Register', @level2type = N'COLUMN', @level2name = N'Company_Code';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Register') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Register'), N'Shareholder_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東代碼，與公司代碼組成主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Register', @level2type = N'COLUMN', @level2name = N'Shareholder_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Register') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Register'), N'Share_Qty', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'目前持股股數，單位為股，可為零。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Register', @level2type = N'COLUMN', @level2name = N'Share_Qty';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Register') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Register'), N'Pledge_Qty', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'已設定質權股數，不得超過持股。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Register', @level2type = N'COLUMN', @level2name = N'Pledge_Qty';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Register') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Register'), N'Last_Update_Time', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'最近異動時間，以 UTC 儲存。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Register', @level2type = N'COLUMN', @level2name = N'Last_Update_Time';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'公司基準日的不可變股權快照主檔；不以目前名簿回推歷史。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot'), N'Snapshot_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'固定快照識別碼。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot', @level2type = N'COLUMN', @level2name = N'Snapshot_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot'), N'Company_Code', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'快照所屬公司。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot', @level2type = N'COLUMN', @level2name = N'Company_Code';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot'), N'Record_Date', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股權基準日，與公司代碼唯一識別一份快照。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot', @level2type = N'COLUMN', @level2name = N'Record_Date';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot'), N'Created_Time', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'快照建立時間，以 UTC 儲存。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot', @level2type = N'COLUMN', @level2name = N'Created_Time';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日正持股股東及當日姓名地址；通知與股利計算的不可變來源。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot_Detail';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail'), N'Snapshot_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'所屬快照識別碼，與股東代碼組成主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot_Detail', @level2type = N'COLUMN', @level2name = N'Snapshot_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail'), N'Shareholder_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日股東代碼。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot_Detail', @level2type = N'COLUMN', @level2name = N'Shareholder_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail'), N'Shareholder_Name', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日凍結的股東姓名。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot_Detail', @level2type = N'COLUMN', @level2name = N'Shareholder_Name';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail'), N'Address', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日凍結的通訊地址。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot_Detail', @level2type = N'COLUMN', @level2name = N'Address';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail'), N'Share_Qty', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日持股股數，只收錄正持股。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot_Detail', @level2type = N'COLUMN', @level2name = N'Share_Qty';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail'), N'Pledge_Qty', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日已設定質權股數。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholding_Snapshot_Detail', @level2type = N'COLUMN', @level2name = N'Pledge_Qty';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Operation_Request') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'成功股權異動請求及內容指紋，支援相同請求重送且不重複異動。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Operation_Request';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Operation_Request') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Operation_Request'), N'Request_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'呼叫端提供的全域唯一請求識別碼。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Operation_Request', @level2type = N'COLUMN', @level2name = N'Request_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Operation_Request') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Operation_Request'), N'Company_Code', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'本請求操作的公司代碼。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Operation_Request', @level2type = N'COLUMN', @level2name = N'Company_Code';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Operation_Request') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Operation_Request'), N'Payload_Hash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'後端正規化內容的 SHA-256 大寫十六進位字串。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Operation_Request', @level2type = N'COLUMN', @level2name = N'Payload_Hash';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Operation_Request') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Operation_Request'), N'Operator_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'後端取得的操作者；示範身分為 DEMO_OPERATOR。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Operation_Request', @level2type = N'COLUMN', @level2name = N'Operator_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Operation_Request') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Operation_Request'), N'Created_Time', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'成功請求建立時間，以 UTC 儲存。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Operation_Request', @level2type = N'COLUMN', @level2name = N'Created_Time';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股票過戶與質權設定撤銷的逐項稽核紀錄，保存雙方完整異動前後值。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Transfer_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股權異動流水號，由資料庫編號。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Transfer_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Company_Code', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'異動所屬公司，必須與請求公司一致。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Company_Code';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'From_Shareholder', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'出讓股東；質權操作填本人。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'From_Shareholder';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'To_Shareholder', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'受讓股東；質權操作填與出讓人相同的本人。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'To_Shareholder';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Transfer_Qty', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'本次異動正股數，單位為股。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Transfer_Qty';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Transfer_Type', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'異動類型：TRANSFER 過戶、PLEDGE 設質、UNPLEDGE 撤質。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Transfer_Type';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Created_Time', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'異動操作時間，以 UTC 儲存。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Created_Time';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Request_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'所屬成功異動請求。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Request_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Item_No', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'請求內依序執行的項次，從一開始，最多一百。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Item_No';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'Operator_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'後端記錄的操作者。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'Operator_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'From_Share_Qty_Before', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'出讓人異動前的持股股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'From_Share_Qty_Before';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'From_Share_Qty_After', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'出讓人異動後的持股股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'From_Share_Qty_After';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'From_Pledge_Qty_Before', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'出讓人異動前的質押股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'From_Pledge_Qty_Before';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'From_Pledge_Qty_After', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'出讓人異動後的質押股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'From_Pledge_Qty_After';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'To_Share_Qty_Before', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'受讓人異動前的持股股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'To_Share_Qty_Before';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'To_Share_Qty_After', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'受讓人異動後的持股股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'To_Share_Qty_After';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'To_Pledge_Qty_Before', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'受讓人異動前的質押股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'To_Pledge_Qty_Before';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Share_Transfer_Log') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Share_Transfer_Log'), N'To_Pledge_Qty_After', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'受讓人異動後的質押股數；質權操作兩人欄位保存相同狀態。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Share_Transfer_Log', @level2type = N'COLUMN', @level2name = N'To_Pledge_Qty_After';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東會日期、固定基準日快照與通知完成旗標。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholders_Meeting';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholders_Meeting'), N'Meeting_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股東會識別碼。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholders_Meeting', @level2type = N'COLUMN', @level2name = N'Meeting_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholders_Meeting'), N'Company_Code', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'召開股東會的公司代碼。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholders_Meeting', @level2type = N'COLUMN', @level2name = N'Company_Code';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholders_Meeting'), N'Meeting_Type', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'AGM 為常會，EGM 為臨時會。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholders_Meeting', @level2type = N'COLUMN', @level2name = N'Meeting_Type';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholders_Meeting'), N'Meeting_Date', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'開會日期；示範基準日為往前三十個日曆日。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholders_Meeting', @level2type = N'COLUMN', @level2name = N'Meeting_Date';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholders_Meeting'), N'Notice_Sent_Flag', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'是否已完成模擬寄發；與通知明細同交易更新。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholders_Meeting', @level2type = N'COLUMN', @level2name = N'Notice_Sent_Flag';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Shareholders_Meeting') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Shareholders_Meeting'), N'Snapshot_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日快照；空值代表尚無可用快照，通知時須拒絕。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Shareholders_Meeting', @level2type = N'COLUMN', @level2name = N'Snapshot_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'已完成模擬寄發的通知名單；複製基準日資料，重送不得改變內容。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Meeting_Notice_Detail'), N'Meeting_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'所屬股東會，與股東代碼組成主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail', @level2type = N'COLUMN', @level2name = N'Meeting_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Meeting_Notice_Detail'), N'Shareholder_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'通知收件股東。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail', @level2type = N'COLUMN', @level2name = N'Shareholder_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Meeting_Notice_Detail'), N'Shareholder_Name', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'寄發時保存的基準日股東姓名。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail', @level2type = N'COLUMN', @level2name = N'Shareholder_Name';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Meeting_Notice_Detail'), N'Address', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'寄發時保存的基準日通訊地址。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail', @level2type = N'COLUMN', @level2name = N'Address';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Meeting_Notice_Detail'), N'Share_Qty', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'基準日正持股股數。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail', @level2type = N'COLUMN', @level2name = N'Share_Qty';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Meeting_Notice_Detail'), N'Sent_Time', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'模擬寄發完成時間，以 UTC 儲存。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail', @level2type = N'COLUMN', @level2name = N'Sent_Time';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Meeting_Notice_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Meeting_Notice_Detail'), N'Operator_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'執行模擬寄發的後端操作者。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Meeting_Notice_Detail', @level2type = N'COLUMN', @level2name = N'Operator_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'現金或股票股利批次，精確對帳後與凍結明細同交易完成。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Batch_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股利批次唯一識別碼，同一批次不得重複執行。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Batch_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Company_Code', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股利所屬公司。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Company_Code';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Dividend_Type', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'CASH 為現金元，STOCK 為股票股數。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Dividend_Type';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Dividend_Per_Share', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'每股現金金額或配股率，保留四位小數。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Dividend_Per_Share';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Total_Amount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'須與逐戶明細加總完全相符的總額；單位依類型為元或股。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Total_Amount';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Status', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'零為待處理，一為已完成模擬配發。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Status';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Snapshot_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'計算股利的不可變基準日快照；空值時不可確認。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Snapshot_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Completed_Time', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'模擬配發完成時間，以 UTC 儲存；待處理為空。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Completed_Time';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Batch') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Batch'), N'Operator_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'完成模擬配發的後端操作者；待處理為空。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Batch', @level2type = N'COLUMN', @level2name = N'Operator_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Detail') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'已確認股利的凍結逐戶計算結果，不回寫目前持股。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Detail';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Detail'), N'Batch_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'所屬股利批次，與股東代碼組成主鍵。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Detail', @level2type = N'COLUMN', @level2name = N'Batch_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Detail'), N'Shareholder_ID', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'股利受配股東。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Detail', @level2type = N'COLUMN', @level2name = N'Shareholder_ID';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Detail'), N'Shareholder_Name', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'計算時保存的基準日股東姓名。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Detail', @level2type = N'COLUMN', @level2name = N'Shareholder_Name';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Detail'), N'Share_Qty', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'用於計算的基準日正持股股數。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Detail', @level2type = N'COLUMN', @level2name = N'Share_Qty';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Detail'), N'Dividend_Per_Share', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'凍結的每股金額或配股率。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Detail', @level2type = N'COLUMN', @level2name = N'Dividend_Per_Share';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.Dividend_Detail') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Dividend_Detail'), N'Dividend_Value', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'逐戶現金元或股票股數；股票只接受整股。',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Dividend_Detail', @level2type = N'COLUMN', @level2name = N'Dividend_Value';

COMMIT TRANSACTION;
GO
