namespace Kgi.Api.Features.Lending;

/// <summary>
/// 借券監控同一份查詢結果的統計與合約明細。
/// </summary>
public sealed record LendingMonitorResponse
{
    /// <summary>
    /// 行情交易日，格式為 yyyy-MM-dd；尚未匯入任何行情時為 null。
    /// </summary>
    public string? TradeDate { get; init; }

    /// <summary>
    /// 全部借券合約件數，包含尚無價格而無法計算的合約。
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// 維持率大於或等於 140% 的正常合約件數。
    /// </summary>
    public int NormalCount { get; init; }

    /// <summary>
    /// 維持率大於或等於 120%、小於 140% 的預警合約件數。
    /// </summary>
    public int WarningCount { get; init; }

    /// <summary>
    /// 維持率小於 120% 的處置合約件數。
    /// </summary>
    public int CallCount { get; init; }

    /// <summary>
    /// 缺少指定交易日行情而尚未計算的合約件數，不計入正常件數。
    /// </summary>
    public int UnpricedCount { get; init; }

    /// <summary>
    /// 全部合約明細；無合約時為空陣列，篩選及分頁由畫面處理。
    /// </summary>
    public List<LendingContractResponse> Contracts { get; init; } = [];
}

/// <summary>
/// 單筆借券合約及 SQL 計算結果，金融數值以十進位字串傳輸。
/// </summary>
public sealed record LendingContractResponse
{
    /// <summary>
    /// 借券合約編號，最多二十個字元。
    /// </summary>
    public string ContractNo { get; init; } = string.Empty;

    /// <summary>
    /// 借出股票代碼，最多十個字元，保留前導零。
    /// </summary>
    public string StockId { get; init; } = string.Empty;

    /// <summary>
    /// 借出數量（張），一張為一千股，為正整數。
    /// </summary>
    public int BorrowedQty { get; init; }

    /// <summary>
    /// 擔保品總市值（元），以固定兩位小數的十進位字串表示。
    /// </summary>
    public string CollateralValue { get; init; } = string.Empty;

    /// <summary>
    /// 所選交易日收盤價（元），以固定四位小數的十進位字串表示；缺價時為 null。
    /// </summary>
    public string? ClosingPrice { get; init; }

    /// <summary>
    /// 維持率（%），以固定兩位小數的十進位字串表示；缺價時為 null。狀態以未格式化數值判斷。
    /// </summary>
    public string? MaintenanceRatio { get; init; }

    /// <summary>
    /// 監控狀態：Normal 正常、Warning 預警、Call 處置、Unpriced 尚無價格。
    /// </summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// 行情整批匯入成功後的結果；完成後可重新取得監控總覽。
/// </summary>
public sealed record LendingImportResponse
{
    /// <summary>
    /// 本次匯入的行情交易日，格式為 yyyy-MM-dd。
    /// </summary>
    public string TradeDate { get; init; } = string.Empty;

    /// <summary>
    /// 成功匯入的股票行情筆數，同日原有行情已由本批完整替換。
    /// </summary>
    public int ImportedCount { get; init; }
}
