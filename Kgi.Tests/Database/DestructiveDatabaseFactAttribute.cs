namespace Kgi.Tests.Database;

/// <summary>
/// 破壞式資料庫案例須額外授權，不隨一般資料庫測試啟用。
/// </summary>
public sealed class DestructiveDatabaseFactAttribute : FactAttribute
{
    /// <summary>
    /// 同時要求一般測試條件與破壞式操作旗標，未明確允許時跳過案例。
    /// </summary>
    public DestructiveDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KGI_ALLOW_DB_REBUILD") != "1" || !DatabaseTestConfiguration.IsEnabled)
        {
            Skip = "此案例會清空或重建共用 KGI 中的資料；需同時設定 KGI_RUN_DB_TESTS=1、KGI_ALLOW_DB_REBUILD=1 與 ConnectionStrings:Kgi 才可執行。";
        }
    }
}
