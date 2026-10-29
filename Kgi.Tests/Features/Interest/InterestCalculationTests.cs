using Kgi.Api.Features.Interest;

namespace Kgi.Tests.Features.Interest;

/// <summary>
/// 驗證示範計息規則及金融小數邊界，不依賴資料庫。
/// </summary>
public sealed class InterestCalculationTests
{
    /// <summary>
    /// 年利率採小數表示，逐筆四捨五入至四位，中點遠離零。
    /// </summary>
    [Theory]
    [InlineData("100000", "0.03", "8.2192")]
    [InlineData("365", "0.00005", "0.0001")]
    [InlineData("365", "0.00015", "0.0002")]
    [InlineData("0.0001", "0.01", "0.0000")]
    public void DailyInterestUsesDecimalAndRoundsEachContract(string principal, string rate, string expected)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(decimal.Parse(expected, culture), InterestService.CalculateDailyInterest(
            decimal.Parse(principal, culture), decimal.Parse(rate, culture)));
    }

    /// <summary>
    /// 非正值或超出來源欄位精度的輸入，在寫入帳務之前拒絕。
    /// </summary>
    [Theory]
    [InlineData("0", "0.03")]
    [InlineData("-1", "0.03")]
    [InlineData("100", "0")]
    [InlineData("100", "-0.01")]
    [InlineData("100000000000000", "0.03")]
    [InlineData("100.00001", "0.03")]
    [InlineData("100", "10")]
    [InlineData("100", "0.000000001")]
    public void InvalidSourceValuesAreRejected(string principal, string rate)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.ThrowsAny<ArgumentException>(() => InterestService.CalculateDailyInterest(
            decimal.Parse(principal, culture), decimal.Parse(rate, culture)));
    }
}
