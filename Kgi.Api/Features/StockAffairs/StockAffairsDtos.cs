namespace Kgi.Api.Features.StockAffairs;

/// <summary>
/// 公司名簿總覽，聚合股數以字串保留精度。
/// </summary>
public sealed record StockCompanyResponse
{
    /// <summary>
    /// 公司代碼。
    /// </summary>
    public string CompanyCode { get; init; } = string.Empty;

    /// <summary>
    /// 公司名稱。
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// 正持股股東人數。
    /// </summary>
    public int ShareholderCount { get; init; }

    /// <summary>
    /// 總持股股數，整數字串。
    /// </summary>
    public string TotalShares { get; init; } = string.Empty;

    /// <summary>
    /// 已質押股數，整數字串。
    /// </summary>
    public string PledgedShares { get; init; } = string.Empty;

    /// <summary>
    /// 已質押比例，百分比固定兩位小數字串。
    /// </summary>
    public string PledgedRatio { get; init; } = string.Empty;

}

/// <summary>
/// 目前公司股東名簿，身分欄位僅提供遮罩。
/// </summary>
public sealed record StockShareholderResponse
{
    /// <summary>
    /// 股東代碼。
    /// </summary>
    public string ShareholderId { get; init; } = string.Empty;

    /// <summary>
    /// 股東姓名。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 已遮罩的測試身分證或統編。
    /// </summary>
    public string MaskedIdNo { get; init; } = string.Empty;

    /// <summary>
    /// 通訊地址。
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// 持股股數，非負整數。
    /// </summary>
    public int ShareQty { get; init; }

    /// <summary>
    /// 質押股數，非負整數。
    /// </summary>
    public int PledgeQty { get; init; }

    /// <summary>
    /// 可用股數，持股減質押。
    /// </summary>
    public int AvailableQty { get; init; }

}

/// <summary>
/// 可作受讓人的股東主檔。
/// </summary>
public sealed record StockCandidateResponse
{
    /// <summary>
    /// 股東代碼。
    /// </summary>
    public string ShareholderId { get; init; } = string.Empty;

    /// <summary>
    /// 股東姓名。
    /// </summary>
    public string Name { get; init; } = string.Empty;

}

/// <summary>
/// 指定公司的名簿與全體股東候選清單。
/// </summary>
public sealed record StockRegisterResponse
{
    /// <summary>
    /// 公司代碼。
    /// </summary>
    public string CompanyCode { get; init; } = string.Empty;

    /// <summary>
    /// 目前名簿，包含零持股者。
    /// </summary>
    public List<StockShareholderResponse> Shareholders { get; init; } = [];

    /// <summary>
    /// 可供受讓的股東主檔。
    /// </summary>
    public List<StockCandidateResponse> Candidates { get; init; } = [];

}

/// <summary>
/// 單筆或最多一百筆股權異動請求。
/// </summary>
public sealed record StockTransferRequest
{
    /// <summary>
    /// 防重送識別碼，一至六十四字元。
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// 依輸入順序處理的異動明細。
    /// </summary>
    public IReadOnlyList<StockTransferItem> Items { get; init; } = [];

}

/// <summary>
/// 一筆過戶或質權異動。
/// </summary>
public sealed record StockTransferItem
{
    /// <summary>
    /// 異動類型，TRANSFER、PLEDGE、UNPLEDGE。
    /// </summary>
    public string TransferType { get; init; } = string.Empty;

    /// <summary>
    /// 出讓人或質權操作股東。
    /// </summary>
    public string FromShareholder { get; init; } = string.Empty;

    /// <summary>
    /// 受讓人；質權操作時可省略或為 null。
    /// </summary>
    public string? ToShareholder { get; init; }

    /// <summary>
    /// 異動股數，必須為正整數。
    /// </summary>
    public int Quantity { get; init; }

}

/// <summary>
/// 異動交易執行結果。
/// </summary>
public sealed record StockTransferResult
{
    /// <summary>
    /// 本次請求識別碼。
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// 成功的異動明細筆數。
    /// </summary>
    public int TransferCount { get; init; }

    /// <summary>
    /// 是否為相同請求的既有結果。
    /// </summary>
    public bool AlreadyApplied { get; init; }

}

/// <summary>
/// 股權異動與前後值稽核紀錄。
/// </summary>
public sealed record StockTransferResponse
{
    /// <summary>
    /// 異動流水號。
    /// </summary>
    public int TransferId { get; init; }

    /// <summary>
    /// 異動請求識別碼。
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// 異動類型。
    /// </summary>
    public string TransferType { get; init; } = string.Empty;

    /// <summary>
    /// 出讓人或質權股東。
    /// </summary>
    public string FromShareholder { get; init; } = string.Empty;

    /// <summary>
    /// 受讓人；質權時為同一股東。
    /// </summary>
    public string ToShareholder { get; init; } = string.Empty;

    /// <summary>
    /// 異動股數。
    /// </summary>
    public int TransferQty { get; init; }

    /// <summary>
    /// 出讓人異動前持股。
    /// </summary>
    public int FromShareQtyBefore { get; init; }

    /// <summary>
    /// 出讓人異動後持股。
    /// </summary>
    public int FromShareQtyAfter { get; init; }

    /// <summary>
    /// 出讓人異動前質押。
    /// </summary>
    public int FromPledgeQtyBefore { get; init; }

    /// <summary>
    /// 出讓人異動後質押。
    /// </summary>
    public int FromPledgeQtyAfter { get; init; }

    /// <summary>
    /// 受讓人異動前持股。
    /// </summary>
    public int ToShareQtyBefore { get; init; }

    /// <summary>
    /// 受讓人異動後持股。
    /// </summary>
    public int ToShareQtyAfter { get; init; }

    /// <summary>
    /// 受讓人異動前質押。
    /// </summary>
    public int ToPledgeQtyBefore { get; init; }

    /// <summary>
    /// 受讓人異動後質押。
    /// </summary>
    public int ToPledgeQtyAfter { get; init; }

    /// <summary>
    /// 後端記錄的示範操作者。
    /// </summary>
    public string OperatorId { get; init; } = string.Empty;

    /// <summary>
    /// 操作時間，ISO 8601 UTC。
    /// </summary>
    public string CreatedTime { get; init; } = string.Empty;

}

/// <summary>
/// 股東會列表與通知狀態。
/// </summary>
public sealed record StockMeetingResponse
{
    /// <summary>
    /// 會議代碼。
    /// </summary>
    public string MeetingId { get; init; } = string.Empty;

    /// <summary>
    /// 會議類型 AGM 或 EGM。
    /// </summary>
    public string MeetingType { get; init; } = string.Empty;

    /// <summary>
    /// 會議日期，yyyy-MM-dd。
    /// </summary>
    public string MeetingDate { get; init; } = string.Empty;

    /// <summary>
    /// 會議前三十日的示範基準日，yyyy-MM-dd。
    /// </summary>
    public string RecordDate { get; init; } = string.Empty;

    /// <summary>
    /// 是否完成模擬通知。
    /// </summary>
    public bool NoticeSent { get; init; }

    /// <summary>
    /// 正持股收件股東人數。
    /// </summary>
    public int RecipientCount { get; init; }

}

/// <summary>
/// 基準日通知收件資料。
/// </summary>
public sealed record StockNoticeResponse
{
    /// <summary>
    /// 股東代碼。
    /// </summary>
    public string ShareholderId { get; init; } = string.Empty;

    /// <summary>
    /// 基準日股東姓名。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 基準日通知地址。
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// 基準日持股股數。
    /// </summary>
    public int ShareQty { get; init; }

    /// <summary>
    /// 模擬通知時間，UTC；未寄發為 null。
    /// </summary>
    public string? SentTime { get; init; }

}

/// <summary>
/// 股東會通知預覽或已寄發明細。
/// </summary>
public sealed record StockNoticesResponse
{
    /// <summary>
    /// 會議代碼。
    /// </summary>
    public string MeetingId { get; init; } = string.Empty;

    /// <summary>
    /// 是否已完成模擬通知。
    /// </summary>
    public bool NoticeSent { get; init; }

    /// <summary>
    /// 固定基準日的收件名單。
    /// </summary>
    public List<StockNoticeResponse> Recipients { get; init; } = [];

}

/// <summary>
/// 股利批次清單。
/// </summary>
public sealed record StockDividendResponse
{
    /// <summary>
    /// 批次代碼。
    /// </summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>
    /// CASH 現金或 STOCK 股票。
    /// </summary>
    public string DividendType { get; init; } = string.Empty;

    /// <summary>
    /// 每股配發金額或配股率，四位小數字串。
    /// </summary>
    public string DividendPerShare { get; init; } = string.Empty;

    /// <summary>
    /// 主檔預計總元或總股數，兩位小數字串。
    /// </summary>
    public string TotalAmount { get; init; } = string.Empty;

    /// <summary>
    /// TWD 為元，SHARE 為股。
    /// </summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// 0 待處理，1 完成。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 基準日，yyyy-MM-dd；缺快照時為空字串。
    /// </summary>
    public string RecordDate { get; init; } = string.Empty;

}

/// <summary>
/// 逐戶股利試算或凍結明細。
/// </summary>
public sealed record StockDividendDetailResponse
{
    /// <summary>
    /// 股東代碼。
    /// </summary>
    public string ShareholderId { get; init; } = string.Empty;

    /// <summary>
    /// 基準日股東姓名。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 基準日持股股數。
    /// </summary>
    public int ShareQty { get; init; }

    /// <summary>
    /// 配發金額或股數，兩位小數字串。
    /// </summary>
    public string DividendValue { get; init; } = string.Empty;

}

/// <summary>
/// 逐戶股利試算與精確對帳結果。
/// </summary>
public sealed record StockDividendPreview
{
    /// <summary>
    /// 批次代碼。
    /// </summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>
    /// CASH 現金或 STOCK 股票。
    /// </summary>
    public string DividendType { get; init; } = string.Empty;

    /// <summary>
    /// 每股金額或配股率，四位小數。
    /// </summary>
    public string DividendPerShare { get; init; } = string.Empty;

    /// <summary>
    /// 主檔預計總額，兩位小數。
    /// </summary>
    public string TotalAmount { get; init; } = string.Empty;

    /// <summary>
    /// 逐戶加總結果，兩位小數。
    /// </summary>
    public string CalculatedTotal { get; init; } = string.Empty;

    /// <summary>
    /// TWD 為元，SHARE 為股。
    /// </summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// 逐戶總額是否精確等於主檔。
    /// </summary>
    public bool MatchesExpected { get; init; }

    /// <summary>
    /// 是否尚未完成且所有驗證皆通過。
    /// </summary>
    public bool CanConfirm { get; init; }

    /// <summary>
    /// 阻擋確認的原因；可確認時為 null。
    /// </summary>
    public string? ValidationMessage { get; init; }

    /// <summary>
    /// 依股東代碼排序的明細。
    /// </summary>
    public List<StockDividendDetailResponse> Details { get; init; } = [];

}

/// <summary>
/// 股利確認結果。
/// </summary>
public sealed record StockDividendResult
{
    /// <summary>
    /// 股利批次代碼。
    /// </summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>
    /// 完成後固定為 1。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 完成總額，兩位小數字串。
    /// </summary>
    public string TotalAmount { get; init; } = string.Empty;

    /// <summary>
    /// 凍結股利明細筆數。
    /// </summary>
    public int DetailCount { get; init; }

    /// <summary>
    /// 是否回傳既有完成結果。
    /// </summary>
    public bool AlreadyCompleted { get; init; }

}
