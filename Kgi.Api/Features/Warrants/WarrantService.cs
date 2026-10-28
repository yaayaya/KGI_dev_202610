using System.Globalization;
using Kgi.Api.Data.Entities;

namespace Kgi.Api.Features.Warrants;

/// <summary>
/// 負責權證輸入驗證、金融計算、作業流程與回應格式，透過 DAO 存取主檔與試算紀錄。
/// </summary>
public sealed class WarrantService(WarrantDao dao)
{
    /// <summary>
    /// 股價與理論價值可儲存於 DECIMAL(18,4) 的最大值。
    /// </summary>
    private const decimal MaxPrice = 99999999999999.9999m;

    /// <summary>
    /// 避險張數可儲存於 DECIMAL(18,2) 的最大值。
    /// </summary>
    private const decimal MaxHedgeQty = 9999999999999999.99m;

    /// <summary>
    /// 依權證代碼排序查詢基本資料；有關鍵字時先去除前後空白，再比對代碼是否包含關鍵字。
    /// </summary>
    public async Task<List<WarrantResponse>> GetWarrantsAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        var search = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var warrants = await dao.GetWarrantsAsync(search, cancellationToken);
        var responses = new List<WarrantResponse>();
        foreach (var warrant in warrants)
        {
            responses.Add(new WarrantResponse
            {
                WarrantId = warrant.WarrantId,
                StrikePrice = Format(warrant.StrikePrice, 4),
                ConversionRatio = Format(warrant.ConversionRatio, 4),
                WarrantType = warrant.WarrantType,
                PositionQty = warrant.PositionQty
            });
        }

        return responses;
    }

    /// <summary>
    /// 驗證股價並依權證主檔預覽試算結果，不寫入紀錄，流水號與存檔時間皆為空值。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">股價格式無效，或計算結果超出資料庫可儲存範圍。</exception>
    /// <exception cref="KeyNotFoundException">找不到指定的權證。</exception>
    /// <exception cref="InvalidOperationException">權證主檔資料不符合計算條件。</exception>
    public async Task<TrialResponse> PreviewAsync(
        string warrantId, TrialRequest request, CancellationToken cancellationToken = default)
    {
        ValidateMarketPrice(request.MarketPrice);
        var warrant = await GetWarrantAsync(warrantId, cancellationToken);
        var result = Calculate(warrant, request.MarketPrice);
        return new TrialResponse
        {
            WarrantId = warrant.WarrantId,
            MarketPrice = Format(request.MarketPrice, 4),
            TheoryPrice = Format(result.TheoryPrice, 4),
            HedgeQty = Format(result.HedgeQty, 2)
        };
    }

    /// <summary>
    /// 驗證股價並依權證主檔重新計算後儲存，回傳包含資料庫流水號與 UTC 存檔時間的結果。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">股價格式無效，或計算結果超出資料庫可儲存範圍。</exception>
    /// <exception cref="KeyNotFoundException">找不到指定的權證。</exception>
    /// <exception cref="InvalidOperationException">權證主檔資料不符合計算條件。</exception>
    public async Task<TrialResponse> SaveTrialAsync(
        string warrantId, TrialRequest request, CancellationToken cancellationToken = default)
    {
        ValidateMarketPrice(request.MarketPrice);
        var warrant = await GetWarrantAsync(warrantId, cancellationToken);
        var result = Calculate(warrant, request.MarketPrice);
        var trial = await dao.InsertTrialAsync(new WarrantTrialLog
        {
            WarrantId = warrant.WarrantId,
            MarketPrice = request.MarketPrice,
            TheoryPrice = result.TheoryPrice,
            HedgeQty = result.HedgeQty
        }, cancellationToken);
        return ToResponse(trial);
    }

    /// <summary>
    /// 確認權證存在後，取得最近十筆試算紀錄；依存檔時間倒序，同時間再依流水號倒序排列。
    /// </summary>
    /// <exception cref="KeyNotFoundException">找不到指定的權證。</exception>
    public async Task<List<TrialResponse>> GetRecentTrialsAsync(
        string warrantId, CancellationToken cancellationToken = default)
    {
        await GetWarrantAsync(warrantId, cancellationToken);
        var trials = await dao.GetRecentTrialsAsync(warrantId, cancellationToken);
        var responses = new List<TrialResponse>();
        foreach (var trial in trials)
        {
            responses.Add(ToResponse(trial));
        }

        return responses;
    }

    /// <summary>
    /// 依 CALL／PUT 價差判斷價內、價平、價外，Delta 分別採正值 0.8、0.5、0.2。
    /// 理論價值為非負價差乘行使比例，避險張數為庫存張數乘行使比例與 Delta；
    /// 分別四捨五入至四位、兩位小數，中點採遠離零的取位方式。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">股價格式無效，或計算結果超出資料庫可儲存範圍。</exception>
    /// <exception cref="InvalidOperationException">履約價或行使比例非正數、庫存為負數，或類型不是 CALL／PUT。</exception>
    internal static TrialCalculation Calculate(WarrantMaster warrant, decimal marketPrice)
    {
        ValidateMarketPrice(marketPrice);
        if (warrant.StrikePrice <= 0 || warrant.ConversionRatio <= 0 || warrant.PositionQty < 0)
        {
            throw new InvalidOperationException("權證基本資料不符合計算條件。");
        }

        decimal priceDifference;
        if (warrant.WarrantType == "CALL")
        {
            priceDifference = marketPrice - warrant.StrikePrice;
        }
        else if (warrant.WarrantType == "PUT")
        {
            priceDifference = warrant.StrikePrice - marketPrice;
        }
        else
        {
            throw new InvalidOperationException("權證類型必須為 CALL 或 PUT。");
        }

        // 依題目簡化模型，CALL 與 PUT 的 Delta 均使用正值。
        decimal delta;
        if (priceDifference > 0)
        {
            delta = 0.8m;
        }
        else if (priceDifference == 0)
        {
            delta = 0.5m;
        }
        else
        {
            delta = 0.2m;
        }

        var theoryPrice = decimal.Round(Math.Max(0m, priceDifference * warrant.ConversionRatio),
            4, MidpointRounding.AwayFromZero);
        var hedgeQty = decimal.Round(warrant.PositionQty * warrant.ConversionRatio * delta,
            2, MidpointRounding.AwayFromZero);

        if (theoryPrice > MaxPrice || hedgeQty > MaxHedgeQty)
        {
            throw new ArgumentOutOfRangeException(nameof(marketPrice),
                "依目前權證參數計算的結果超出資料庫可儲存範圍。");
        }

        return new TrialCalculation(theoryPrice, hedgeQty);
    }

    /// <summary>
    /// 股價必須大於零、最多四位小數，且不超過 DECIMAL(18,4) 的可儲存上限。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">股價不符合上述限制。</exception>
    private static void ValidateMarketPrice(decimal marketPrice)
    {
        if (marketPrice <= 0 || marketPrice > MaxPrice || decimal.Round(marketPrice, 4) != marketPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(marketPrice),
                "標的股價必須大於 0、最多四位小數，且不超過 99999999999999.9999。");
        }
    }

    /// <summary>
    /// 透過 DAO 取得指定權證，找不到時拋出例外，供全域例外處理回傳 404。
    /// </summary>
    /// <exception cref="KeyNotFoundException">找不到指定的權證。</exception>
    private async Task<WarrantMaster> GetWarrantAsync(
        string warrantId, CancellationToken cancellationToken)
    {
        var warrant = await dao.GetWarrantAsync(warrantId, cancellationToken);
        if (warrant is null)
        {
            throw new KeyNotFoundException("找不到指定的權證。");
        }

        return warrant;
    }

    /// <summary>
    /// 將試算紀錄轉成回應，價格保留四位、避險張數保留兩位小數，並將資料庫時間標示為 UTC。
    /// </summary>
    private static TrialResponse ToResponse(WarrantTrialLog trial)
    {
        return new TrialResponse
        {
            LogId = trial.LogId,
            WarrantId = trial.WarrantId,
            MarketPrice = Format(trial.MarketPrice, 4),
            TheoryPrice = Format(trial.TheoryPrice, 4),
            HedgeQty = Format(trial.HedgeQty, 2),
            CreatedTime = DateTime.SpecifyKind(trial.CreatedTime, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// 以固定小數位與不受地區設定影響的格式輸出十進位字串，保留 API 財務數值精度。
    /// </summary>
    private static string Format(decimal value, int decimals)
    {
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// 權證試算的數值結果，供預覽回應與儲存紀錄共用。
/// </summary>
/// <param name="TheoryPrice">權證理論價值（元），已取至四位小數。</param>
/// <param name="HedgeQty">建議避險張數（張），已取至兩位小數。</param>
internal readonly record struct TrialCalculation(decimal TheoryPrice, decimal HedgeQty);
