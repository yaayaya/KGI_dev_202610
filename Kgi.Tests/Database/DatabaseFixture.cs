using Kgi.Tests.Database.Scripts;

namespace Kgi.Tests.Database;

/// <summary>
/// 共用單一 KGI 資料庫，避免資料庫案例彼此並行修改。
/// </summary>
[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

/// <summary>
/// 在明確啟用重建後，使用與 API 相同的 KGI 連線重建功能資料。
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// 取得 API 共用連線，不導向其他資料庫。
    /// </summary>
    public string ConnectionString => DatabaseTestConfiguration.ConnectionString
        ?? throw new InvalidOperationException("請透過 API 設定檔、User Secrets 或環境變數設定 ConnectionStrings:Kgi，並指定 KGI 資料庫。");

    /// <summary>
    /// 依重建開關初始化單一 KGI 的資料表、索引與假資料。
    /// </summary>
    public async Task InitializeAsync()
    {
        if (DatabaseFactAttribute.IsEnabled)
        {
            await InitTestDbTool.RebuildAsync(ConnectionString);
        }
    }

    /// <summary>
    /// 測試結束後保留 KGI，供實際功能操作及資料核對。
    /// </summary>
    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// 未明確啟用 KGI 重建或尚未設定 API 共用連線時，標示資料庫案例為跳過。
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    /// <summary>
    /// 判斷是否具備重建 KGI 的明確開關與共用連線。
    /// </summary>
    public static bool IsEnabled => DatabaseTestConfiguration.IsEnabled;

    /// <summary>
    /// 顯示尚未啟用資料庫案例的具體條件。
    /// </summary>
    public DatabaseFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = "資料庫測試會重建單一 KGI；請明確設定 KGI_ALLOW_DB_REBUILD=1，並使用 API 共用的 ConnectionStrings:Kgi。";
        }
    }
}
