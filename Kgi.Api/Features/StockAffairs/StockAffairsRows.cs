namespace Kgi.Api.Features.StockAffairs;

/// <summary>
/// 公司統計的資料庫查詢結果。
/// </summary>
public sealed class StockCompanyRow
{
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public int ShareholderCount { get; set; }
    public long TotalShares { get; set; }
    public long PledgedShares { get; set; }
}

/// <summary>
/// 股東名簿與主檔的查詢結果，完整身分資料只在服務端遮罩。
/// </summary>
public sealed class StockHolderRow
{
    public string ShareholderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IdNo { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int ShareQty { get; set; }
    public int PledgeQty { get; set; }
}

/// <summary>
/// 交易內鎖定及更新的公司股東部位。
/// </summary>
public sealed class StockPositionRow
{
    public string ShareholderId { get; set; } = string.Empty;
    public int ShareQty { get; set; }
    public int PledgeQty { get; set; }
}

/// <summary>
/// 已成功寫入的防重送請求資料。
/// </summary>
public sealed class StockRequestRow
{
    public string RequestId { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public int TransferCount { get; set; }
    public bool AlreadyApplied { get; set; }
}

/// <summary>
/// 股權異動紀錄與前後值映射。
/// </summary>
public sealed class StockTransferRow
{
    public int TransferId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public int ItemNo { get; set; }
    public string TransferType { get; set; } = string.Empty;
    public string FromShareholder { get; set; } = string.Empty;
    public string ToShareholder { get; set; } = string.Empty;
    public int TransferQty { get; set; }
    public int FromShareQtyBefore { get; set; }
    public int FromShareQtyAfter { get; set; }
    public int FromPledgeQtyBefore { get; set; }
    public int FromPledgeQtyAfter { get; set; }
    public int ToShareQtyBefore { get; set; }
    public int ToShareQtyAfter { get; set; }
    public int ToPledgeQtyBefore { get; set; }
    public int ToPledgeQtyAfter { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }
}

/// <summary>
/// 會議、已選快照與收件人數的查詢結果。
/// </summary>
public sealed class StockMeetingRow
{
    public string MeetingId { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string MeetingType { get; set; } = string.Empty;
    public DateTime MeetingDate { get; set; }
    public bool NoticeSent { get; set; }
    public string? SnapshotId { get; set; }
    public DateTime? RecordDate { get; set; }
    public int RecipientCount { get; set; }
}

/// <summary>
/// 不可變基準日股東快照。
/// </summary>
public sealed class StockSnapshotRow
{
    public string ShareholderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int ShareQty { get; set; }
    public int PledgeQty { get; set; }
}

/// <summary>
/// 已寄發的模擬通知明細。
/// </summary>
public sealed class StockNoticeRow
{
    public string ShareholderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int ShareQty { get; set; }
    public DateTime? SentTime { get; set; }
}

/// <summary>
/// 股利批次及關聯快照。
/// </summary>
public sealed class StockDividendRow
{
    public string BatchId { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string DividendType { get; set; } = string.Empty;
    public decimal DividendPerShare { get; set; }
    public decimal TotalAmount { get; set; }
    public int Status { get; set; }
    public string? SnapshotId { get; set; }
    public DateTime? RecordDate { get; set; }
}

/// <summary>
/// 逐戶精確計算與凍結的股利明細。
/// </summary>
public sealed class StockDividendDetailRow
{
    public string ShareholderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ShareQty { get; set; }
    public decimal DividendPerShare { get; set; }
    public decimal DividendValue { get; set; }
}

/// <summary>
/// 名簿讀取時的公司存在狀態、明細與股東候選清單。
/// </summary>
public sealed record StockRegisterData(bool CompanyExists, List<StockHolderRow> Holders, List<StockCandidateResponse> Candidates);

/// <summary>
/// 交由 Service 在交易鎖內驗證的現有部位與全部合法股東代碼。
/// </summary>
public sealed record StockTransferState(bool CompanyExists, List<StockPositionRow> Positions, HashSet<string> ShareholderIds);

/// <summary>
/// Service 驗證完成後交由 Dao 寫入的部位與稽核明細。
/// </summary>
public sealed record StockTransferPlan(List<StockPositionRow> Positions, List<StockTransferRow> Logs);

/// <summary>
/// 會議通知所需的固定快照或已寄發資料。
/// </summary>
public sealed record StockNoticesData(StockMeetingRow Meeting, List<StockSnapshotRow> Snapshot, List<StockNoticeRow> Notices);

/// <summary>
/// 股利批次、不可變快照與既有完成明細。
/// </summary>
public sealed record StockDividendData(StockDividendRow Batch, List<StockSnapshotRow> Snapshot, List<StockDividendDetailRow> Details);

/// <summary>
/// Service 的精確股利計算結果，供預覽及交易內確認共用。
/// </summary>
public sealed record StockDividendCalculation(List<StockDividendDetailRow> Details, decimal Total, string? Error);

/// <summary>
/// 股利確認交易的完成資料及重送狀態。
/// </summary>
public sealed record StockDividendWriteData(StockDividendRow Batch, int DetailCount, bool AlreadyCompleted);
