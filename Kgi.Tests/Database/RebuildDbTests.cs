using Dapper;
using Kgi.Tests.Database.Scripts;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database;

/// <summary>
/// 獨立執行資料庫重建，避免共用 fixture 預先重建，也避免與其他資料庫測試並行。
/// </summary>
[CollectionDefinition("DatabaseRebuild", DisableParallelization = true)]
public sealed class DatabaseRebuildCollection;

/// <summary>
/// 提供可從測試總管單獨執行的單一 KGI 重建入口。
/// </summary>
[Collection("DatabaseRebuild")]
[Trait("Category", "DatabaseReset")]
public sealed class RebuildDbTests
{
    /// <summary>
    /// 明確授權後重建 Q1、Q2、Q3、Q4，核對初始數量、公司部位、歷史快照與股利控制總額。
    /// </summary>
    /// <remarks>
    /// 只有此測試需要重建開關：在測試執行環境設定 KGI_ALLOW_DB_REBUILD=1。
    /// 連線沿用 Kgi.Api/appsettings.json 的 ConnectionStrings:Kgi；執行會清空既有資料，完成後請移除開關。
    /// </remarks>
    [DestructiveDatabaseFact]
    public async Task RebuildDb()
    {
        var connectionString = DatabaseTestConfiguration.ConnectionString
            ?? throw new InvalidOperationException("請設定 API 共用的 ConnectionStrings:Kgi，並精確指定 KGI 資料庫。");

        await InitTestDbTool.RebuildAsync(connectionString);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT COUNT(*) FROM dbo.Warrant_Master;
            SELECT COUNT(*) FROM dbo.Warrant_Trial_Log;
            SELECT COUNT(*) FROM dbo.Lending_Contract;
            SELECT COUNT(*) FROM dbo.Market_Price;
            SELECT COUNT(*) FROM dbo.Interest_Batch;
            SELECT COUNT(*) FROM dbo.Interest_Source;
            SELECT COUNT(*) FROM dbo.Interest_Detail;
            SELECT COUNT(*) FROM dbo.Interest_Batch
            WHERE Process_Status = 0 AND Progress_Rate = 0
                AND Started_Time IS NULL AND Finished_Time IS NULL AND Error_Message IS NULL;
            SELECT COUNT(*) FROM dbo.Company_Master;
            SELECT COUNT(*) FROM dbo.Shareholder_Master;
            SELECT COUNT(*) FROM dbo.Shareholding_Register;
            SELECT COUNT(*) FROM dbo.Shareholding_Snapshot;
            SELECT COUNT(*) FROM dbo.Shareholding_Snapshot_Detail;
            SELECT COUNT(*) FROM dbo.Share_Operation_Request;
            SELECT COUNT(*) FROM dbo.Share_Transfer_Log;
            SELECT COUNT(*) FROM dbo.Shareholders_Meeting;
            SELECT COUNT(*) FROM dbo.Dividend_Batch;
            SELECT COUNT(*) FROM dbo.Meeting_Notice_Detail;
            SELECT COUNT(*) FROM dbo.Dividend_Detail;

            SELECT Company_Code AS CompanyCode,
                SUM(CASE WHEN Share_Qty > 0 THEN 1 ELSE 0 END) AS HolderCount,
                SUM(CASE WHEN Share_Qty = 0 THEN 1 ELSE 0 END) AS ZeroHolderCount,
                SUM(CAST(Share_Qty AS BIGINT)) AS TotalShareQty,
                SUM(CAST(Pledge_Qty AS BIGINT)) AS TotalPledgeQty
            FROM dbo.Shareholding_Register
            GROUP BY Company_Code
            ORDER BY Company_Code;

            SELECT Shareholder_ID AS ShareholderId, Share_Qty AS ShareQty, Pledge_Qty AS PledgeQty
            FROM dbo.Shareholding_Register
            WHERE Company_Code = 'DEMO03' AND Shareholder_ID <= 'SH0010'
            ORDER BY Shareholder_ID;

            SELECT Snapshot_ID AS SnapshotId, Shareholder_ID AS ShareholderId,
                Share_Qty AS ShareQty, Pledge_Qty AS PledgeQty
            FROM dbo.Shareholding_Snapshot_Detail;

            SELECT Batch_ID AS BatchId, Company_Code AS CompanyCode,
                Dividend_Type AS DividendType, Dividend_Per_Share AS DividendPerShare,
                Total_Amount AS TotalAmount, Status, Snapshot_ID AS SnapshotId,
                Completed_Time AS CompletedTime, Operator_ID AS OperatorId
            FROM dbo.Dividend_Batch;

            SELECT m.Meeting_ID AS MeetingId, m.Company_Code AS CompanyCode,
                m.Meeting_Type AS MeetingType, m.Meeting_Date AS MeetingDate,
                m.Notice_Sent_Flag AS NoticeSentFlag, s.Record_Date AS RecordDate
            FROM dbo.Shareholders_Meeting AS m
            INNER JOIN dbo.Shareholding_Snapshot AS s
                ON s.Snapshot_ID = m.Snapshot_ID AND s.Company_Code = m.Company_Code;

            SELECT Company_Code AS CompanyCode, From_Shareholder AS FromShareholder,
                To_Shareholder AS ToShareholder, Transfer_Qty AS TransferQty,
                Transfer_Type AS TransferType, Request_ID AS RequestId,
                From_Share_Qty_Before AS FromShareQtyBefore,
                From_Share_Qty_After AS FromShareQtyAfter,
                From_Pledge_Qty_Before AS FromPledgeQtyBefore,
                From_Pledge_Qty_After AS FromPledgeQtyAfter,
                To_Share_Qty_Before AS ToShareQtyBefore,
                To_Share_Qty_After AS ToShareQtyAfter,
                To_Pledge_Qty_Before AS ToPledgeQtyBefore,
                To_Pledge_Qty_After AS ToPledgeQtyAfter
            FROM dbo.Share_Transfer_Log;

            SELECT Payload_Hash FROM dbo.Share_Operation_Request
            WHERE Request_ID = 'Q4-HISTORY-20261015';

            SELECT COUNT(*)
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo'
                AND t.name IN (N'Company_Master', N'Shareholder_Master', N'Shareholding_Register',
                    N'Shareholding_Snapshot', N'Shareholding_Snapshot_Detail', N'Share_Operation_Request',
                    N'Share_Transfer_Log', N'Shareholders_Meeting', N'Meeting_Notice_Detail',
                    N'Dividend_Batch', N'Dividend_Detail')
                AND NOT EXISTS (SELECT 1 FROM sys.extended_properties AS p
                    WHERE p.major_id = c.object_id AND p.minor_id = c.column_id
                        AND p.name = N'MS_Description');

            SELECT Batch.Batch_ID AS BatchId, Batch.Expected_Amt AS ExpectedAmt,
                Batch.Process_Status AS ProcessStatus, Batch.Progress_Rate AS ProgressRate,
                Batch.Started_Time AS StartedTime, Batch.Finished_Time AS FinishedTime,
                Batch.Error_Message AS ErrorMessage, COUNT(Source.Contract_No) AS SourceCount,
                SUM(CAST(ROUND(Source.Principal_Amt * Source.Annual_Rate / 365.0, 4) AS DECIMAL(18,4))) AS CalculatedAmt
            FROM dbo.Interest_Batch AS Batch
            LEFT JOIN dbo.Interest_Source AS Source ON Source.Batch_ID = Batch.Batch_ID
            GROUP BY Batch.Batch_ID, Batch.Expected_Amt, Batch.Process_Status, Batch.Progress_Rate,
                Batch.Started_Time, Batch.Finished_Time, Batch.Error_Message;
            """));

        Assert.Equal(800, await results.ReadSingleAsync<int>());
        Assert.Equal(0, await results.ReadSingleAsync<int>());
        Assert.Equal(1000, await results.ReadSingleAsync<int>());
        Assert.Equal(0, await results.ReadSingleAsync<int>());
        Assert.Equal(3, await results.ReadSingleAsync<int>());
        Assert.Equal(1015, await results.ReadSingleAsync<int>());
        Assert.Equal(0, await results.ReadSingleAsync<int>());
        Assert.Equal(3, await results.ReadSingleAsync<int>());

        // Q4 只預載歷史過戶，不預先產生通知或股利完成結果。
        foreach (var count in new[] { 3, 300, 506, 6, 1000, 1, 1, 6, 8, 0, 0 })
        {
            Assert.Equal(count, await results.ReadSingleAsync<int>());
        }

        var companies = (await results.ReadAsync<SeedCompany>()).ToDictionary(company => company.CompanyCode);
        var expectedCompanies = new Dictionary<string, (int Holders, long Shares, long Pledged)>
        {
            ["DEMO01"] = (240, 235257700, 90214835),
            ["DEMO02"] = (160, 62018900, 22935495),
            ["DEMO03"] = (100, 11322, 1366)
        };
        Assert.Equal(3, companies.Count);
        foreach (var (companyCode, expected) in expectedCompanies)
        {
            var actual = companies[companyCode];
            Assert.Equal(expected.Holders, actual.HolderCount);
            Assert.Equal(2, actual.ZeroHolderCount);
            Assert.Equal(expected.Shares, actual.TotalShareQty);
            Assert.Equal(expected.Pledged, actual.TotalPledgeQty);
        }

        var currentFixedCases = (await results.ReadAsync<SeedSnapshotDetail>()).ToList();
        Assert.Equal(Enumerable.Range(1, 10).Select(number => $"SH{number:0000}"),
            currentFixedCases.Select(detail => detail.ShareholderId));
        Assert.Equal(new[] { (1, 0), (2, 0), (3, 1), (5, 5), (10, 0), (99, 9),
                (100, 100), (101, 1), (1000, 250), (1001, 1000) },
            currentFixedCases.Select(detail => (detail.ShareQty, detail.PledgeQty)));

        var snapshots = (await results.ReadAsync<SeedSnapshotDetail>())
            .GroupBy(detail => detail.SnapshotId)
            .ToDictionary(group => group.Key, group => group.ToList());
        Assert.Equal(6, snapshots.Count);
        foreach (var (companyCode, expected) in expectedCompanies)
        {
            var label = companyCode == "DEMO01" ? "A" : companyCode == "DEMO02" ? "B" : "C";
            foreach (var date in new[] { "20260930", "20261028" })
            {
                var details = snapshots[$"S-{label}-{date}"];
                Assert.Equal(expected.Holders, details.Count);
                Assert.Equal(expected.Shares, details.Sum(detail => (long)detail.ShareQty));
                Assert.Equal(expected.Pledged, details.Sum(detail => (long)detail.PledgeQty));
                Assert.All(details, detail => Assert.True(detail.ShareQty > 0));
            }
        }

        // 兩個基準日總股數相同，但丙公司逐戶捨入必須呈現一分差異。
        Assert.Equal(100, snapshots["S-C-20260930"].Single(detail => detail.ShareholderId == "SH0006").ShareQty);
        Assert.Equal(100, snapshots["S-C-20260930"].Single(detail => detail.ShareholderId == "SH0008").ShareQty);
        Assert.Equal(99, snapshots["S-C-20261028"].Single(detail => detail.ShareholderId == "SH0006").ShareQty);
        Assert.Equal(101, snapshots["S-C-20261028"].Single(detail => detail.ShareholderId == "SH0008").ShareQty);
        Assert.Equal(56.63m, snapshots["S-C-20260930"].Sum(detail => decimal.Round(detail.ShareQty * 0.0050m, 2, MidpointRounding.AwayFromZero)));
        Assert.Equal(56.64m, snapshots["S-C-20261028"].Sum(detail => decimal.Round(detail.ShareQty * 0.0050m, 2, MidpointRounding.AwayFromZero)));

        var batches = (await results.ReadAsync<SeedDividendBatch>()).ToDictionary(batch => batch.BatchId);
        var expectedBatches = new Dictionary<string, (string Snapshot, string Type, decimal Rate, decimal Total)>
        {
            ["DA-CASH-20260930"] = ("S-A-20260930", "CASH", 1.2500m, 294072125.00m),
            ["DA-STOCK-20260930"] = ("S-A-20260930", "STOCK", 0.1000m, 23525770.00m),
            ["DB-CASH-20261028"] = ("S-B-20261028", "CASH", 0.7500m, 46514175.00m),
            ["DB-STOCK-20261028"] = ("S-B-20261028", "STOCK", 0.0500m, 3100945.00m),
            ["DC-CASH-20261028"] = ("S-C-20261028", "CASH", 0.0050m, 56.64m),
            ["DC-STOCK-20260930"] = ("S-C-20260930", "STOCK", 1.0000m, 11322.00m),
            ["DC-CASH-BADTOTAL"] = ("S-C-20261028", "CASH", 0.0050m, 56.61m),
            ["DC-STOCK-FRACTION"] = ("S-C-20261028", "STOCK", 0.5000m, 5661.00m)
        };
        Assert.Equal(8, batches.Count);
        foreach (var (batchId, expected) in expectedBatches)
        {
            var actual = batches[batchId];
            Assert.Equal(expected.Snapshot, actual.SnapshotId);
            Assert.Equal(expected.Type, actual.DividendType);
            Assert.Equal(expected.Rate, actual.DividendPerShare);
            Assert.Equal(expected.Total, actual.TotalAmount);
            Assert.Equal(0, actual.Status);
            Assert.Null(actual.CompletedTime);
            Assert.Null(actual.OperatorId);

            var values = snapshots[actual.SnapshotId]
                .Select(detail => detail.ShareQty * actual.DividendPerShare).ToList();
            if (actual.DividendType == "CASH")
            {
                var total = values.Sum(value => decimal.Round(value, 2, MidpointRounding.AwayFromZero));
                if (batchId == "DC-CASH-BADTOTAL")
                {
                    Assert.Equal(56.64m, total);
                    Assert.NotEqual(actual.TotalAmount, total);
                }
                else
                {
                    Assert.Equal(actual.TotalAmount, total);
                }
            }
            else if (batchId == "DC-STOCK-FRACTION")
            {
                Assert.Contains(values, value => value != decimal.Truncate(value));
            }
            else
            {
                Assert.All(values, value => Assert.Equal(decimal.Truncate(value), value));
                Assert.Equal(actual.TotalAmount, values.Sum());
            }
        }

        var meetings = (await results.ReadAsync<SeedMeeting>()).ToList();
        Assert.Equal(6, meetings.Count);
        Assert.All(meetings, meeting =>
        {
            Assert.False(meeting.NoticeSentFlag);
            Assert.Equal(meeting.MeetingDate.AddDays(-30), meeting.RecordDate);
            Assert.Equal(meeting.MeetingType == "AGM" ? new DateTime(2026, 10, 30) : new DateTime(2026, 11, 27), meeting.MeetingDate);
        });

        var historical = await results.ReadSingleAsync<SeedTransfer>();
        Assert.Equal("DEMO03", historical.CompanyCode);
        Assert.Equal("SH0006", historical.FromShareholder);
        Assert.Equal("SH0008", historical.ToShareholder);
        Assert.Equal("TRANSFER", historical.TransferType);
        Assert.Equal("Q4-HISTORY-20261015", historical.RequestId);
        Assert.Equal(1, historical.TransferQty);
        Assert.Equal((100, 99, 9, 9), (historical.FromShareQtyBefore, historical.FromShareQtyAfter, historical.FromPledgeQtyBefore, historical.FromPledgeQtyAfter));
        Assert.Equal((100, 101, 1, 1), (historical.ToShareQtyBefore, historical.ToShareQtyAfter, historical.ToPledgeQtyBefore, historical.ToPledgeQtyAfter));
        Assert.Equal("CA5F22600964A05735645C1AAEB9161D5D1841BE3D23EBE8FC8F0DC8A2771B82", await results.ReadSingleAsync<string>());
        Assert.Equal(0, await results.ReadSingleAsync<int>());

        var interestBatches = (await results.ReadAsync<SeedInterestBatch>()).ToDictionary(batch => batch.BatchId);
        var expectedInterest = new Dictionary<string, (int Sources, decimal Expected, decimal Calculated)>
        {
            ["20261029_INT"] = (1000, 22230.2344m, 22230.2344m),
            ["20261028_INT"] = (10, 441.3006m, 441.3004m),
            ["20261027_INT"] = (5, 720.4026m, 720.4026m)
        };
        Assert.Equal(3, interestBatches.Count);
        foreach (var (batchId, expected) in expectedInterest)
        {
            var actual = interestBatches[batchId];
            Assert.Equal(expected.Sources, actual.SourceCount);
            Assert.Equal(expected.Expected, actual.ExpectedAmt);
            Assert.Equal(expected.Calculated, actual.CalculatedAmt);
            Assert.Equal(0, actual.ProcessStatus);
            Assert.Equal(0, actual.ProgressRate);
            Assert.Null(actual.StartedTime);
            Assert.Null(actual.FinishedTime);
            Assert.Null(actual.ErrorMessage);
        }
    }

    /// <summary>
    /// 承接公司控制股數與零股名簿數量，避免聚合股數降為 INT。
    /// </summary>
    private sealed class SeedCompany
    {
        public string CompanyCode { get; set; } = "";
        public int HolderCount { get; set; }
        public int ZeroHolderCount { get; set; }
        public long TotalShareQty { get; set; }
        public long TotalPledgeQty { get; set; }
    }

    /// <summary>
    /// 承接不可變快照的核算欄位。
    /// </summary>
    private sealed class SeedSnapshotDetail
    {
        public string SnapshotId { get; set; } = "";
        public string ShareholderId { get; set; } = "";
        public int ShareQty { get; set; }
        public int PledgeQty { get; set; }
    }

    /// <summary>
    /// 承接批次主檔，以獨立計算核對固定預載的有效與無效案例。
    /// </summary>
    private sealed class SeedDividendBatch
    {
        public string BatchId { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string DividendType { get; set; } = "";
        public decimal DividendPerShare { get; set; }
        public decimal TotalAmount { get; set; }
        public int Status { get; set; }
        public string SnapshotId { get; set; } = "";
        public DateTime? CompletedTime { get; set; }
        public string? OperatorId { get; set; }
    }

    /// <summary>
    /// 承接會議日期與快照日期，核對三十天示範規則。
    /// </summary>
    private sealed class SeedMeeting
    {
        public string MeetingId { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string MeetingType { get; set; } = "";
        public DateTime MeetingDate { get; set; }
        public bool NoticeSentFlag { get; set; }
        public DateTime RecordDate { get; set; }
    }

    /// <summary>
    /// 承接唯一歷史過戶的前後股數，確認預載沒有再次套用異動。
    /// </summary>
    private sealed class SeedTransfer
    {
        public string CompanyCode { get; set; } = "";
        public string FromShareholder { get; set; } = "";
        public string ToShareholder { get; set; } = "";
        public int TransferQty { get; set; }
        public string TransferType { get; set; } = "";
        public string RequestId { get; set; } = "";
        public int FromShareQtyBefore { get; set; }
        public int FromShareQtyAfter { get; set; }
        public int FromPledgeQtyBefore { get; set; }
        public int FromPledgeQtyAfter { get; set; }
        public int ToShareQtyBefore { get; set; }
        public int ToShareQtyAfter { get; set; }
        public int ToPledgeQtyBefore { get; set; }
        public int ToPledgeQtyAfter { get; set; }
    }

    /// <summary>
    /// 承接 Q3 三批來源數、預計總額與初始狀態，保留故意總額不符的案例。
    /// </summary>
    private sealed class SeedInterestBatch
    {
        public string BatchId { get; set; } = "";
        public decimal ExpectedAmt { get; set; }
        public int ProcessStatus { get; set; }
        public int ProgressRate { get; set; }
        public DateTime? StartedTime { get; set; }
        public DateTime? FinishedTime { get; set; }
        public string? ErrorMessage { get; set; }
        public int SourceCount { get; set; }
        public decimal CalculatedAmt { get; set; }
    }
}
