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

-- Q4：依公司讀取異動歷史、股東會與股利批次。
CREATE INDEX IX_Share_Transfer_Log_Company_Time_ID
    ON dbo.Share_Transfer_Log (Company_Code, Created_Time DESC, Transfer_ID DESC)
    INCLUDE (Request_ID, Item_No, Transfer_Type, From_Shareholder, To_Shareholder, Transfer_Qty);

CREATE INDEX IX_Shareholders_Meeting_Company_Date_ID
    ON dbo.Shareholders_Meeting (Company_Code, Meeting_Date DESC, Meeting_ID)
    INCLUDE (Meeting_Type, Notice_Sent_Flag, Snapshot_ID);

CREATE INDEX IX_Dividend_Batch_Company_ID
    ON dbo.Dividend_Batch (Company_Code, Batch_ID)
    INCLUDE (Dividend_Type, Status, Snapshot_ID);
GO
