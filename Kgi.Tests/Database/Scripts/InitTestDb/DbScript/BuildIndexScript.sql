-- 建表完成後建立索引，涵蓋單一權證最近十筆試算紀錄的查詢。
-- 流水號確保建立時間相同時，查詢結果仍有穩定的排序。
CREATE INDEX IX_Warrant_Trial_Log_Warrant_ID_Created_Time_Log_ID
    ON dbo.Warrant_Trial_Log (Warrant_ID, Created_Time DESC, Log_ID DESC)
    INCLUDE (Market_Price, Theory_Price, Hedge_Qty);
GO

-- 按股票代碼對照同交易日行情，涵蓋批次維持率查詢所需的部位與擔保品欄位。
CREATE INDEX IX_Lending_Contract_Stock_ID
    ON dbo.Lending_Contract (Stock_ID)
    INCLUDE (Borrowed_Qty, Collateral_Val);
GO

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

GO
