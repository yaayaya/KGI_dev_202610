namespace Kgi.Tests.Database;

/// <summary>
/// 僅供資料庫重建案例使用，避免一般執行全部測試時清空 KGI。
/// </summary>
public sealed class DestructiveDatabaseFactAttribute : FactAttribute
{
    /// <summary>
    /// 同時要求共用連線與重建旗標，未明確允許時跳過重建。
    /// </summary>
    public DestructiveDatabaseFactAttribute()
    {
        if (!DatabaseTestConfiguration.AllowDbRebuild || !DatabaseTestConfiguration.IsEnabled)
        {
            Skip = "此案例會重建並清空 KGI；請先設定 appsettings.json 的 ConnectionStrings:Kgi，並明確啟用 KGI_ALLOW_DB_REBUILD=1。";
        }
    }
}
