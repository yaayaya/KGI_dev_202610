namespace Kgi.Api.Features.StockAffairs;

/// <summary>
/// 集中股務查詢、鎖定及批次寫入的明確參數化 SQL。
/// </summary>
public static class StockAffairsSql
{
    public const string Companies = """
        SELECT c.Company_Code AS CompanyCode, c.Company_Name AS CompanyName,
               COUNT(CASE WHEN r.Share_Qty > 0 THEN 1 END) AS ShareholderCount,
               COALESCE(SUM(CONVERT(BIGINT,r.Share_Qty)),0) AS TotalShares,
               COALESCE(SUM(CONVERT(BIGINT,r.Pledge_Qty)),0) AS PledgedShares
        FROM dbo.Company_Master c
        LEFT JOIN dbo.Shareholding_Register r ON r.Company_Code=c.Company_Code
        GROUP BY c.Company_Code,c.Company_Name ORDER BY c.Company_Code;
        """;

    public const string CompanyExists = "SELECT CONVERT(BIT,CASE WHEN EXISTS (SELECT 1 FROM dbo.Company_Master WHERE Company_Code=@CompanyCode) THEN 1 ELSE 0 END);";

    public const string Register = """
        SELECT r.Shareholder_ID AS ShareholderId, s.Shareholder_Name AS Name,
               s.ID_No AS IdNo, s.Address, r.Share_Qty AS ShareQty,r.Pledge_Qty AS PledgeQty
        FROM dbo.Shareholding_Register r
        JOIN dbo.Shareholder_Master s ON s.Shareholder_ID=r.Shareholder_ID
        WHERE r.Company_Code=@CompanyCode ORDER BY r.Shareholder_ID;
        """;

    public const string Candidates = "SELECT Shareholder_ID AS ShareholderId,Shareholder_Name AS Name FROM dbo.Shareholder_Master ORDER BY Shareholder_ID;";

    public const string Lock = """
        DECLARE @Result INT;
        EXEC @Result=sys.sp_getapplock @Resource=@LockResource,@LockMode='Exclusive',
             @LockOwner='Transaction',@LockTimeout=15000;
        SELECT @Result;
        """;

    public const string ExistingRequest = """
        SELECT r.Request_ID AS RequestId,r.Company_Code AS CompanyCode,r.Payload_Hash AS PayloadHash,
               (SELECT COUNT(*) FROM dbo.Share_Transfer_Log l WHERE l.Request_ID=r.Request_ID) AS TransferCount
        FROM dbo.Share_Operation_Request r WITH (UPDLOCK,HOLDLOCK) WHERE r.Request_ID=@RequestId;
        """;

    public const string Positions = """
        SELECT Shareholder_ID AS ShareholderId,Share_Qty AS ShareQty,Pledge_Qty AS PledgeQty
        FROM dbo.Shareholding_Register WITH (UPDLOCK,HOLDLOCK)
        WHERE Company_Code=@CompanyCode ORDER BY Shareholder_ID;
        """;

    public const string WriteTransfers = """
        INSERT INTO dbo.Share_Operation_Request(Request_ID,Company_Code,Payload_Hash,Operator_ID,Created_Time)
        VALUES(@RequestId,@CompanyCode,@PayloadHash,@OperatorId,@Now);

        UPDATE r SET Share_Qty=p.ShareQty,Pledge_Qty=p.PledgeQty,Last_Update_Time=@Now
        FROM dbo.Shareholding_Register r
        JOIN OPENJSON(@Positions) WITH(ShareholderId VARCHAR(20),ShareQty INT,PledgeQty INT) p
          ON p.ShareholderId=r.Shareholder_ID
        WHERE r.Company_Code=@CompanyCode;

        INSERT INTO dbo.Shareholding_Register(Company_Code,Shareholder_ID,Share_Qty,Pledge_Qty,Last_Update_Time)
        SELECT @CompanyCode,p.ShareholderId,p.ShareQty,p.PledgeQty,@Now
        FROM OPENJSON(@Positions) WITH(ShareholderId VARCHAR(20),ShareQty INT,PledgeQty INT) p
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Shareholding_Register r
                          WHERE r.Company_Code=@CompanyCode AND r.Shareholder_ID=p.ShareholderId);

        INSERT INTO dbo.Share_Transfer_Log
          (Company_Code,From_Shareholder,To_Shareholder,Transfer_Qty,Transfer_Type,Created_Time,
           Request_ID,Item_No,Operator_ID,From_Share_Qty_Before,From_Share_Qty_After,
           From_Pledge_Qty_Before,From_Pledge_Qty_After,To_Share_Qty_Before,To_Share_Qty_After,
           To_Pledge_Qty_Before,To_Pledge_Qty_After)
        SELECT @CompanyCode,l.FromShareholder,l.ToShareholder,l.TransferQty,l.TransferType,@Now,
               @RequestId,l.ItemNo,@OperatorId,l.FromShareQtyBefore,l.FromShareQtyAfter,
               l.FromPledgeQtyBefore,l.FromPledgeQtyAfter,l.ToShareQtyBefore,l.ToShareQtyAfter,
               l.ToPledgeQtyBefore,l.ToPledgeQtyAfter
        FROM OPENJSON(@Logs) WITH
          (FromShareholder VARCHAR(20),ToShareholder VARCHAR(20),TransferQty INT,TransferType VARCHAR(10),
           ItemNo INT,FromShareQtyBefore INT,FromShareQtyAfter INT,FromPledgeQtyBefore INT,
           FromPledgeQtyAfter INT,ToShareQtyBefore INT,ToShareQtyAfter INT,ToPledgeQtyBefore INT,ToPledgeQtyAfter INT) l;
        """;

    public const string Transfers = """
        SELECT Transfer_ID AS TransferId,Request_ID AS RequestId,Item_No AS ItemNo,
               Transfer_Type AS TransferType,From_Shareholder AS FromShareholder,To_Shareholder AS ToShareholder,
               Transfer_Qty AS TransferQty,From_Share_Qty_Before AS FromShareQtyBefore,
               From_Share_Qty_After AS FromShareQtyAfter,From_Pledge_Qty_Before AS FromPledgeQtyBefore,
               From_Pledge_Qty_After AS FromPledgeQtyAfter,To_Share_Qty_Before AS ToShareQtyBefore,
               To_Share_Qty_After AS ToShareQtyAfter,To_Pledge_Qty_Before AS ToPledgeQtyBefore,
               To_Pledge_Qty_After AS ToPledgeQtyAfter,Operator_ID AS OperatorId,Created_Time AS CreatedTime
        FROM dbo.Share_Transfer_Log WHERE Company_Code=@CompanyCode
        ORDER BY Created_Time DESC,Transfer_ID DESC;
        """;

    public const string Meetings = """
        SELECT m.Meeting_ID AS MeetingId,m.Company_Code AS CompanyCode,m.Meeting_Type AS MeetingType,
               m.Meeting_Date AS MeetingDate,m.Notice_Sent_Flag AS NoticeSent,m.Snapshot_ID AS SnapshotId,
               s.Record_Date AS RecordDate,
               CASE WHEN m.Notice_Sent_Flag=1
                    THEN (SELECT COUNT(*) FROM dbo.Meeting_Notice_Detail n WHERE n.Meeting_ID=m.Meeting_ID)
                    ELSE (SELECT COUNT(*) FROM dbo.Shareholding_Snapshot_Detail d WHERE d.Snapshot_ID=m.Snapshot_ID)
               END AS RecipientCount
        FROM dbo.Shareholders_Meeting m
        LEFT JOIN dbo.Shareholding_Snapshot s ON s.Snapshot_ID=m.Snapshot_ID AND s.Company_Code=m.Company_Code
        WHERE m.Company_Code=@CompanyCode
        """;

    public const string Snapshot = """
        SELECT Shareholder_ID AS ShareholderId,Shareholder_Name AS Name,Address,
               Share_Qty AS ShareQty,Pledge_Qty AS PledgeQty
        FROM dbo.Shareholding_Snapshot_Detail WHERE Snapshot_ID=@SnapshotId ORDER BY Shareholder_ID;
        """;

    public const string Notices = """
        SELECT Shareholder_ID AS ShareholderId,Shareholder_Name AS Name,Address,Share_Qty AS ShareQty,Sent_Time AS SentTime
        FROM dbo.Meeting_Notice_Detail WITH (UPDLOCK,HOLDLOCK) WHERE Meeting_ID=@MeetingId ORDER BY Shareholder_ID;
        """;

    public const string WriteNotices = """
        INSERT INTO dbo.Meeting_Notice_Detail
          (Meeting_ID,Shareholder_ID,Shareholder_Name,Address,Share_Qty,Sent_Time,Operator_ID)
        SELECT @MeetingId,Shareholder_ID,Shareholder_Name,Address,Share_Qty,@Now,@OperatorId
        FROM dbo.Shareholding_Snapshot_Detail WHERE Snapshot_ID=@SnapshotId AND Share_Qty>0;
        UPDATE dbo.Shareholders_Meeting SET Notice_Sent_Flag=1
        WHERE Meeting_ID=@MeetingId AND Company_Code=@CompanyCode;
        """;

    public const string Dividends = """
        SELECT b.Batch_ID AS BatchId,b.Company_Code AS CompanyCode,b.Dividend_Type AS DividendType,
               b.Dividend_Per_Share AS DividendPerShare,b.Total_Amount AS TotalAmount,b.Status,
               b.Snapshot_ID AS SnapshotId,s.Record_Date AS RecordDate
        FROM dbo.Dividend_Batch b
        LEFT JOIN dbo.Shareholding_Snapshot s ON s.Snapshot_ID=b.Snapshot_ID AND s.Company_Code=b.Company_Code
        WHERE b.Company_Code=@CompanyCode
        """;

    public const string DividendDetails = """
        SELECT Shareholder_ID AS ShareholderId,Shareholder_Name AS Name,Share_Qty AS ShareQty,
               Dividend_Per_Share AS DividendPerShare,Dividend_Value AS DividendValue
        FROM dbo.Dividend_Detail WITH (UPDLOCK,HOLDLOCK) WHERE Batch_ID=@BatchId ORDER BY Shareholder_ID;
        """;

    public const string WriteDividend = """
        INSERT INTO dbo.Dividend_Detail
          (Batch_ID,Shareholder_ID,Shareholder_Name,Share_Qty,Dividend_Per_Share,Dividend_Value)
        SELECT @BatchId,d.ShareholderId,d.Name,d.ShareQty,d.DividendPerShare,d.DividendValue
        FROM OPENJSON(@Details) WITH
          (ShareholderId VARCHAR(20),Name NVARCHAR(50),ShareQty INT,
           DividendPerShare DECIMAL(18,4),DividendValue DECIMAL(18,2)) d;
        UPDATE dbo.Dividend_Batch SET Status=1,Completed_Time=@Now,Operator_ID=@OperatorId
        WHERE Batch_ID=@BatchId AND Company_Code=@CompanyCode AND Status=0;
        """;
}
