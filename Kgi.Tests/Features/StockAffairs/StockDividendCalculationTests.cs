using Kgi.Api.Features.StockAffairs;

namespace Kgi.Tests.Features.StockAffairs;

/// <summary>
/// 不連資料庫驗證逐戶捨入、整股限制、欄位上限與輸入快照的不可變性。
/// </summary>
public sealed class StockDividendCalculationTests
{
    /// <summary>
    /// 兩位一股股東各配 0.005 元應先各取 0.01，再加總成 0.02 元。
    /// </summary>
    [Fact]
    public void CashRoundsEachHolderBeforeSumming()
    {
        var data = Data("CASH", 0.0050m, 0.02m, 1, 1);

        var result = StockAffairsService.CalculateDividend(data);

        Assert.Null(result.Error);
        Assert.Equal(0.02m, result.Total);
        Assert.All(result.Details, row => Assert.Equal(0.01m, row.DividendValue));
        Assert.NotEqual(decimal.Round(2 * 0.0050m, 2, MidpointRounding.AwayFromZero), result.Total);
    }

    /// <summary>
    /// 即使股票股利總數是整數，也不能把兩位股東各半股當成可發放的一股。
    /// </summary>
    [Fact]
    public void StockRejectsFractionalHoldersEvenWhenAggregateIsWhole()
    {
        var result = StockAffairsService.CalculateDividend(Data("STOCK", 0.5000m, 1m, 1, 1));

        Assert.Equal(1m, result.Total);
        Assert.Contains("非整股", result.Error);
        Assert.All(result.Details, row => Assert.Equal(0.50m, row.DividendValue));
    }

    /// <summary>
    /// 每股上限乘一百股恰好落在 DECIMAL(18,2) 最大值，不能誤判溢位。
    /// </summary>
    [Fact]
    public void CashAcceptsExactDecimalColumnMaximum()
    {
        const decimal maximum = 9999999999999999.99m;
        var result = StockAffairsService.CalculateDividend(Data("CASH", 99999999999999.9999m, maximum, 100));

        Assert.Null(result.Error);
        Assert.Equal(maximum, result.Total);
        Assert.Equal(maximum, Assert.Single(result.Details).DividendValue);
    }

    /// <summary>
    /// 單戶或累計超過資料欄位上限時必須阻擋，不能等 SQL 截斷或寫入部分明細。
    /// </summary>
    [Theory]
    [InlineData(101, 0)]
    [InlineData(100, 100)]
    public void CashRejectsIndividualOrAggregateColumnOverflow(int first, int second)
    {
        var quantities = second == 0 ? new[] { first } : new[] { first, second };
        var result = StockAffairsService.CalculateDividend(Data("CASH", 99999999999999.9999m,
            9999999999999999.99m, quantities));

        Assert.Contains("DECIMAL(18,2)", result.Error);
        Assert.Empty(result.Details);
    }

    /// <summary>
    /// 缺少快照識別碼、快照日期或股東時，不以目前名簿替代。
    /// </summary>
    [Fact]
    public void MissingOrEmptySnapshotIsRejectedExplicitly()
    {
        var missingId = Data("CASH", 1m, 1m, 1);
        missingId.Batch.SnapshotId = null;
        Assert.Contains("缺少基準日快照", StockAffairsService.CalculateDividend(missingId).Error);

        var missingDate = Data("CASH", 1m, 1m, 1);
        missingDate.Batch.RecordDate = null;
        Assert.Contains("缺少基準日快照", StockAffairsService.CalculateDividend(missingDate).Error);

        Assert.Contains("沒有正持股股東", StockAffairsService.CalculateDividend(Data("CASH", 1m, 0m)).Error);
    }

    /// <summary>
    /// 重複預覽不能修改基準日股數、質押、姓名或批次數值。
    /// </summary>
    [Fact]
    public void CalculationLeavesSnapshotAndBatchUnchanged()
    {
        var data = Data("CASH", 0.0050m, 0.51m, 1, 100);
        data.Snapshot[1].PledgeQty = 70;
        var before = data.Snapshot.Select(row => (row.ShareholderId, row.Name, row.ShareQty, row.PledgeQty)).ToArray();

        var first = StockAffairsService.CalculateDividend(data);
        var second = StockAffairsService.CalculateDividend(data);

        Assert.Null(first.Error);
        Assert.Equal(first.Total, second.Total);
        Assert.Equal(before, data.Snapshot.Select(row => (row.ShareholderId, row.Name, row.ShareQty, row.PledgeQty)).ToArray());
        Assert.Empty(data.Details);
        Assert.Equal(0, data.Batch.Status);
        Assert.Equal(0.51m, data.Batch.TotalAmount);
        Assert.Equal(0.0050m, data.Batch.DividendPerShare);
    }

    /// <summary>
    /// 建立不依賴 SQL 的固定批次與正持股快照。
    /// </summary>
    private static StockDividendData Data(string type, decimal rate, decimal total, params int[] quantities) => new(
        new StockDividendRow
        {
            BatchId = "UNIT-BATCH", CompanyCode = "UNIT", DividendType = type,
            DividendPerShare = rate, TotalAmount = total, SnapshotId = "UNIT-SNAPSHOT",
            RecordDate = new DateTime(2026, 10, 28)
        },
        quantities.Select((quantity, index) => new StockSnapshotRow
        {
            ShareholderId = $"SH{index + 1:0000}", Name = $"測試股東{index + 1}",
            Address = "測試市示範區", ShareQty = quantity
        }).ToList(), []);
}
