-- 建表完成後建立索引，涵蓋單一權證最近十筆試算紀錄的查詢。
-- 流水號確保建立時間相同時，查詢結果仍有穩定的排序。
CREATE INDEX IX_Warrant_Trial_Log_Warrant_ID_Created_Time_Log_ID
    ON dbo.Warrant_Trial_Log (Warrant_ID, Created_Time DESC, Log_ID DESC)
    INCLUDE (Market_Price, Theory_Price, Hedge_Qty);
GO
