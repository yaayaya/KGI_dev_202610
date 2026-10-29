using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kgi.Api.Exceptions;

namespace Kgi.Api.Features.StockAffairs;

/// <summary>
/// 管理股務輸入驗證、交易內部位規劃、基準日通知及精確股利計算。
/// </summary>
public sealed class StockAffairsService(StockAffairsDao dao)
{
    private const decimal MaxAmount = 9999999999999999.99m;

    /// <summary>
    /// 取得公司總覽，聚合股數及比例以不受地區影響的字串傳輸。
    /// </summary>
    public async Task<List<StockCompanyResponse>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dao.GetCompaniesAsync(cancellationToken);
        return rows.Select(row => new StockCompanyResponse
        {
            CompanyCode = row.CompanyCode, CompanyName = row.CompanyName, ShareholderCount = row.ShareholderCount,
            TotalShares = row.TotalShares.ToString(CultureInfo.InvariantCulture),
            PledgedShares = row.PledgedShares.ToString(CultureInfo.InvariantCulture),
            PledgedRatio = Format(row.TotalShares == 0 ? 0m : (decimal)row.PledgedShares / row.TotalShares * 100m)
        }).ToList();
    }

    /// <summary>
    /// 取得指定公司名簿，完整身分資料在回傳前遮罩。
    /// </summary>
    public async Task<StockRegisterResponse> GetRegisterAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var company = NormalizeId(companyCode, 10, "公司代碼");
        var data = await dao.GetRegisterAsync(company, cancellationToken);
        if (!data.CompanyExists) throw new KeyNotFoundException("查無指定公司。");
        return new StockRegisterResponse
        {
            CompanyCode = company,
            Candidates = data.Candidates,
            Shareholders = data.Holders.Select(row => new StockShareholderResponse
            {
                ShareholderId = row.ShareholderId, Name = row.Name, Address = row.Address,
                MaskedIdNo = MaskId(row.IdNo), ShareQty = row.ShareQty, PledgeQty = row.PledgeQty,
                AvailableQty = row.ShareQty - row.PledgeQty
            }).ToList()
        };
    }

    /// <summary>
    /// 將股權歷史與前後值轉為 API 明細，時間明確標記 UTC。
    /// </summary>
    public async Task<List<StockTransferResponse>> GetTransfersAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var rows = await dao.GetTransfersAsync(NormalizeId(companyCode, 10, "公司代碼"), cancellationToken)
            ?? throw new KeyNotFoundException("查無指定公司。");
        return rows.Select(row => new StockTransferResponse
        {
            TransferId = row.TransferId, RequestId = row.RequestId, TransferType = row.TransferType,
            FromShareholder = row.FromShareholder, ToShareholder = row.ToShareholder, TransferQty = row.TransferQty,
            FromShareQtyBefore = row.FromShareQtyBefore, FromShareQtyAfter = row.FromShareQtyAfter,
            FromPledgeQtyBefore = row.FromPledgeQtyBefore, FromPledgeQtyAfter = row.FromPledgeQtyAfter,
            ToShareQtyBefore = row.ToShareQtyBefore, ToShareQtyAfter = row.ToShareQtyAfter,
            ToPledgeQtyBefore = row.ToPledgeQtyBefore, ToPledgeQtyAfter = row.ToPledgeQtyAfter,
            OperatorId = row.OperatorId, CreatedTime = Utc(row.CreatedTime)
        }).ToList();
    }

    /// <summary>
    /// 正規化異動與防重送內容，再由 Dao 在同一交易鎖內呼叫部位規劃。
    /// </summary>
    public async Task<StockTransferResult> ApplyTransfersAsync(string companyCode, StockTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        var company = NormalizeId(companyCode, 10, "公司代碼");
        var requestId = NormalizeId(request.RequestId, 64, "請求識別碼");
        if (request.Items is null || request.Items.Count is < 1 or > 100)
            throw new ArgumentException("異動必須包含一至一百筆明細。");
        var items = request.Items.Select(NormalizeTransfer).ToList();
        var canonical = JsonSerializer.Serialize(new
        {
            companyCode = company,
            items = items.Select(item => new { transferType = item.TransferType,
                fromShareholder = item.FromShareholder, toShareholder = item.ToShareholder, quantity = item.Quantity })
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var data = await dao.ApplyTransfersAsync(company, requestId, hash,
            state => BuildTransferPlan(state, items), cancellationToken);
        if (data.CompanyCode != company || !string.Equals(data.PayloadHash, hash, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("此請求識別碼已用於不同公司或不同異動內容，請勿覆用。");
        return new StockTransferResult { RequestId = requestId, TransferCount = data.TransferCount,
            AlreadyApplied = data.AlreadyApplied };
    }

    /// <summary>
    /// 取得會議清單，畫面基準日按會議前三十日的示範規則顯示。
    /// </summary>
    public async Task<List<StockMeetingResponse>> GetMeetingsAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var rows = await dao.GetMeetingsAsync(NormalizeId(companyCode, 10, "公司代碼"), cancellationToken)
            ?? throw new KeyNotFoundException("查無指定公司。");
        return rows.Select(row => new StockMeetingResponse
        {
            MeetingId = row.MeetingId, MeetingType = row.MeetingType, MeetingDate = Date(row.MeetingDate),
            RecordDate = Date(MeetingRecordDate(row.MeetingDate)), NoticeSent = row.NoticeSent, RecipientCount = row.RecipientCount
        }).ToList();
    }

    /// <summary>
    /// 取得基準日通知預覽；已寄發時讀取凍結明細而不重新產生名單。
    /// </summary>
    public async Task<StockNoticesResponse> GetNoticesAsync(string companyCode, string meetingId,
        CancellationToken cancellationToken = default)
    {
        var data = await dao.GetNoticesAsync(NormalizeId(companyCode, 10, "公司代碼"),
            NormalizeId(meetingId, 20, "會議代碼"), cancellationToken)
            ?? throw new KeyNotFoundException("查無此公司所屬的會議。");
        if (!data.Meeting.NoticeSent) ValidateNotices(data);
        return ToNotices(data);
    }

    /// <summary>
    /// 交易內驗證基準日後模擬寄發，重送回傳既有收件人與原通知時間。
    /// </summary>
    public async Task<StockNoticesResponse> SendNoticesAsync(string companyCode, string meetingId,
        CancellationToken cancellationToken = default)
    {
        var data = await dao.SendNoticesAsync(NormalizeId(companyCode, 10, "公司代碼"),
            NormalizeId(meetingId, 20, "會議代碼"), ValidateNotices, cancellationToken)
            ?? throw new KeyNotFoundException("查無此公司所屬的會議。");
        return ToNotices(data);
    }

    /// <summary>
    /// 取得股利批次及金額單位，不在前端重算金融數值。
    /// </summary>
    public async Task<List<StockDividendResponse>> GetDividendsAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var rows = await dao.GetDividendsAsync(NormalizeId(companyCode, 10, "公司代碼"), cancellationToken)
            ?? throw new KeyNotFoundException("查無指定公司。");
        return rows.Select(row => new StockDividendResponse
        {
            BatchId = row.BatchId, DividendType = row.DividendType, DividendPerShare = Format(row.DividendPerShare, 4),
            TotalAmount = Format(row.TotalAmount), Unit = Unit(row.DividendType), Status = row.Status,
            RecordDate = row.RecordDate.HasValue ? Date(row.RecordDate.Value) : string.Empty
        }).ToList();
    }

    /// <summary>
    /// 取得逐戶試算及對帳錯誤；已完成批次固定顯示凍結結果。
    /// </summary>
    public async Task<StockDividendPreview> PreviewDividendAsync(string companyCode, string batchId,
        CancellationToken cancellationToken = default)
    {
        var data = await dao.GetDividendAsync(NormalizeId(companyCode, 10, "公司代碼"),
            NormalizeId(batchId, 20, "股利批次"), cancellationToken)
            ?? throw new KeyNotFoundException("查無此公司所屬的股利批次。");
        var calculation = data.Batch.Status == 1
            ? new StockDividendCalculation(data.Details, data.Details.Sum(row => row.DividendValue), "此批次已完成，顯示原發放明細。")
            : CalculateDividend(data);
        return new StockDividendPreview
        {
            BatchId = data.Batch.BatchId, DividendType = data.Batch.DividendType,
            DividendPerShare = Format(data.Batch.DividendPerShare, 4), TotalAmount = Format(data.Batch.TotalAmount),
            CalculatedTotal = Format(calculation.Total), Unit = Unit(data.Batch.DividendType),
            MatchesExpected = calculation.Total == data.Batch.TotalAmount,
            CanConfirm = data.Batch.Status == 0 && calculation.Error is null,
            ValidationMessage = calculation.Error,
            Details = calculation.Details.Select(row => new StockDividendDetailResponse
            {
                ShareholderId = row.ShareholderId, Name = row.Name, ShareQty = row.ShareQty,
                DividendValue = Format(row.DividendValue)
            }).ToList()
        };
    }

    /// <summary>
    /// 在批次鎖內重算及精確對帳，成功後凍結明細並鎖定狀態。
    /// </summary>
    public async Task<StockDividendResult> ConfirmDividendAsync(string companyCode, string batchId,
        CancellationToken cancellationToken = default)
    {
        var data = await dao.ConfirmDividendAsync(NormalizeId(companyCode, 10, "公司代碼"),
            NormalizeId(batchId, 20, "股利批次"), context =>
            {
                var calculation = CalculateDividend(context);
                if (calculation.Error is not null) throw new ArgumentException(calculation.Error);
                return calculation;
            }, cancellationToken) ?? throw new KeyNotFoundException("查無此公司所屬的股利批次。");
        return new StockDividendResult { BatchId = data.Batch.BatchId, Status = data.Batch.Status,
            TotalAmount = Format(data.Batch.TotalAmount), DetailCount = data.DetailCount,
            AlreadyCompleted = data.AlreadyCompleted };
    }

    /// <summary>
    /// 使用基準日逐戶計算，保留非整股、欄位溢位及主明細總額不符的具體原因。
    /// </summary>
    public static StockDividendCalculation CalculateDividend(StockDividendData data)
    {
        if (data.Batch.SnapshotId is null || data.Batch.RecordDate is null)
            return new([], 0m, "此批次缺少基準日快照，不能使用目前名簿替代。");
        if (data.Snapshot.Count == 0) return new([], 0m, "基準日沒有正持股股東，不能確認發放。");
        if (data.Batch.DividendType is not ("CASH" or "STOCK") || data.Batch.DividendPerShare <= 0)
            return new([], 0m, "股利類型或每股配發資料無效。");
        var details = new List<StockDividendDetailRow>(data.Snapshot.Count);
        decimal total = 0m;
        string? error = null;
        try
        {
            foreach (var holder in data.Snapshot)
            {
                if (holder.ShareQty <= 0) return new([], 0m, "快照包含非正持股資料，不能確認發放。");
                var exact = checked(holder.ShareQty * data.Batch.DividendPerShare);
                if (data.Batch.DividendType == "STOCK" && exact != decimal.Truncate(exact))
                    error ??= $"股東 {holder.ShareholderId} 的配股為非整股，首版尚不處理畸零股。";
                var value = decimal.Round(exact, 2, MidpointRounding.AwayFromZero);
                if (value > MaxAmount) return new([], 0m, "單戶配發結果超過 DECIMAL(18,2) 可儲存範圍。");
                total = checked(total + value);
                if (total > MaxAmount) return new([], 0m, "配發總額超過 DECIMAL(18,2) 可儲存範圍。");
                details.Add(new StockDividendDetailRow { ShareholderId = holder.ShareholderId, Name = holder.Name,
                    ShareQty = holder.ShareQty, DividendPerShare = data.Batch.DividendPerShare, DividendValue = value });
            }
        }
        catch (OverflowException)
        {
            return new([], 0m, "配發計算超過可處理的數值範圍。");
        }
        if (total != data.Batch.TotalAmount)
            error ??= $"逐戶配發總額 {Format(total)} 與主檔 {Format(data.Batch.TotalAmount)} 不符，本次不能發放。";
        return new(details, total, error);
    }

    /// <summary>
    /// 在鎖定的目前部位上依輸入順序模擬整批異動，驗證成功後才交由 Dao 落地。
    /// </summary>
    private static StockTransferPlan BuildTransferPlan(StockTransferState state, List<StockTransferItem> items)
    {
        if (!state.CompanyExists) throw new KeyNotFoundException("查無指定公司。");
        var positions = state.Positions.ToDictionary(row => row.ShareholderId, StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var logs = new List<StockTransferRow>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!state.ShareholderIds.Contains(item.FromShareholder) || !state.ShareholderIds.Contains(item.ToShareholder!))
                throw new ArgumentException($"第 {index + 1} 筆包含不存在的股東主檔。");
            if (!positions.TryGetValue(item.FromShareholder, out var from))
                throw new ArgumentException($"第 {index + 1} 筆出讓人不在此公司名簿。");
            if (!positions.TryGetValue(item.ToShareholder!, out var to))
                to = new StockPositionRow { ShareholderId = item.ToShareholder! };
            var log = new StockTransferRow { ItemNo = index + 1, TransferType = item.TransferType,
                FromShareholder = item.FromShareholder, ToShareholder = item.ToShareholder!, TransferQty = item.Quantity,
                FromShareQtyBefore = from.ShareQty, FromPledgeQtyBefore = from.PledgeQty,
                ToShareQtyBefore = to.ShareQty, ToPledgeQtyBefore = to.PledgeQty };
            if (item.TransferType is "TRANSFER" or "PLEDGE")
            {
                if (item.Quantity > from.ShareQty - from.PledgeQty)
                    throw new ArgumentException($"第 {index + 1} 筆可用股數不足，不能過戶或超額質押。");
                if (item.TransferType == "TRANSFER")
                {
                    if ((long)to.ShareQty + item.Quantity > int.MaxValue)
                        throw new ArgumentException($"第 {index + 1} 筆受讓後持股超過 INT 可儲存範圍。");
                    from.ShareQty -= item.Quantity;
                    to.ShareQty += item.Quantity;
                    positions[to.ShareholderId] = to;
                    changed.Add(to.ShareholderId);
                }
                else from.PledgeQty += item.Quantity;
            }
            else
            {
                if (item.Quantity > from.PledgeQty)
                    throw new ArgumentException($"第 {index + 1} 筆撤銷股數超過已質押股數。");
                from.PledgeQty -= item.Quantity;
            }
            log.FromShareQtyAfter = from.ShareQty;
            log.FromPledgeQtyAfter = from.PledgeQty;
            log.ToShareQtyAfter = to.ShareQty;
            log.ToPledgeQtyAfter = to.PledgeQty;
            changed.Add(from.ShareholderId);
            logs.Add(log);
        }
        return new StockTransferPlan(changed.Order(StringComparer.Ordinal).Select(id => positions[id]).ToList(), logs);
    }

    /// <summary>
    /// 驗證每筆異動基本欄位，質權操作統一以本人作為出讓與受讓股東。
    /// </summary>
    private static StockTransferItem NormalizeTransfer(StockTransferItem item)
    {
        if (item is null) throw new ArgumentException("異動明細不能為 null。");
        var type = item.TransferType?.Trim().ToUpperInvariant();
        if (type is not ("TRANSFER" or "PLEDGE" or "UNPLEDGE")) throw new ArgumentException("異動類型必須為 TRANSFER、PLEDGE 或 UNPLEDGE。");
        if (item.Quantity <= 0) throw new ArgumentException("異動股數必須大於零。");
        var from = NormalizeId(item.FromShareholder, 20, "出讓人");
        var to = type == "TRANSFER" ? NormalizeId(item.ToShareholder, 20, "受讓人") : from;
        if (type == "TRANSFER" && from == to) throw new ArgumentException("股票不能過戶給同一股東。");
        if (type != "TRANSFER" && !string.IsNullOrWhiteSpace(item.ToShareholder)
            && NormalizeId(item.ToShareholder, 20, "質權股東") != from)
            throw new ArgumentException("質權設定或撤銷只操作股東本人，不移轉所有權。");
        return item with { TransferType = type, FromShareholder = from, ToShareholder = to };
    }

    /// <summary>
    /// 驗證示範基準日與不可變快照一致，且至少一位正持股收件人。
    /// </summary>
    private static void ValidateNotices(StockNoticesData data)
    {
        var recordDate = MeetingRecordDate(data.Meeting.MeetingDate);
        if (data.Meeting.SnapshotId is null || data.Meeting.RecordDate?.Date != recordDate)
            throw new ArgumentException("會議前三十日缺少對應快照，不能使用目前名簿替代。");
        if (data.Snapshot.Count == 0) throw new ArgumentException("基準日沒有正持股股東，不能產生通知。");
    }

    /// <summary>
    /// 已寄發時回傳凍結名單，未寄發時顯示固定基準日資料。
    /// </summary>
    private static StockNoticesResponse ToNotices(StockNoticesData data) => new()
    {
        MeetingId = data.Meeting.MeetingId, NoticeSent = data.Meeting.NoticeSent,
        Recipients = data.Meeting.NoticeSent
            ? data.Notices.Select(row => new StockNoticeResponse { ShareholderId = row.ShareholderId,
                Name = row.Name, Address = row.Address, ShareQty = row.ShareQty,
                SentTime = row.SentTime.HasValue ? Utc(row.SentTime.Value) : null }).ToList()
            : data.Snapshot.Select(row => new StockNoticeResponse { ShareholderId = row.ShareholderId,
                Name = row.Name, Address = row.Address, ShareQty = row.ShareQty }).ToList()
    };

    /// <summary>
    /// 識別碼只接受可列印英數及常用分隔字元，統一大寫避免資料庫定序造成重送歧義。
    /// </summary>
    private static string NormalizeId(string? value, int maximumLength, string label)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximumLength
            || !Regex.IsMatch(normalized, "^[A-Z0-9][A-Z0-9._:-]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"{label}須為一至{maximumLength}字元的英數代碼，可含點、底線、冒號與連字號。");
        return normalized;
    }

    /// <summary>
    /// 將身分資料遮罩，只保留最後四碼供辨識。
    /// </summary>
    private static string MaskId(string value) => value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];

    /// <summary>
    /// 以日曆日計算測驗用基準日，日期不足三十日時回傳輸入錯誤。
    /// </summary>
    private static DateTime MeetingRecordDate(DateTime meetingDate)
    {
        if (meetingDate.Date < DateTime.MinValue.AddDays(30)) throw new ArgumentException("會議日期無法計算前三十日基準日。");
        return meetingDate.Date.AddDays(-30);
    }

    /// <summary>
    /// 依配發類型標示元或股，避免將股票股利顯示成金額。
    /// </summary>
    private static string Unit(string type) => type == "CASH" ? "TWD" : "SHARE";

    /// <summary>
    /// 輸出固定小數位的精確十進位字串。
    /// </summary>
    private static string Format(decimal value, int digits = 2) => decimal.Round(value, digits, MidpointRounding.AwayFromZero).ToString($"F{digits}", CultureInfo.InvariantCulture);

    /// <summary>
    /// 輸出不含時間的基準日或會議日期。
    /// </summary>
    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// SQL DATETIME 的 UTC 意義明確標示為 Z 時間。
    /// </summary>
    private static string Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
