using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Database;

/// <summary>
/// 資料庫測試沿用 API 的 Kgi 連線，僅在明確啟用時使用單一 KGI 資料庫。
/// </summary>
public static class DatabaseTestConfiguration
{
    private static readonly Lazy<string?> LazyConnectionString = new(ResolveConnectionString);
    private static readonly Lazy<bool> LazyRunDatabaseTests = new(ResolveRunDatabaseTests);

    /// <summary>
    /// 沿用 API 的 ConnectionStrings:Kgi，不另行切換資料庫或維護測試連線。
    /// </summary>
    public static string? ConnectionString => LazyConnectionString.Value;

    /// <summary>
    /// 判斷是否明確允許建立及清理本次測試擁有的資料；不允許自動重建資料庫。
    /// </summary>
    public static bool RunDatabaseTests => LazyRunDatabaseTests.Value;

    /// <summary>
    /// 僅供手動重建入口使用，不會讓一般資料庫 fixture 自動清表。
    /// </summary>
    public static bool AllowDbRebuild
    {
        get
        {
            var value = LoadConfiguration()["KGI_ALLOW_DB_REBUILD"];
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 判斷資料庫測試是否具備執行條件。
    /// </summary>
    public static bool IsEnabled => RunDatabaseTests && !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>
    /// 載入 API 隨建置複製的設定檔，保留 .NET 的環境變數覆寫方式，不維護另一份測試連線。
    /// </summary>
    private static IConfiguration LoadConfiguration()
    {
        var builder = new ConfigurationBuilder();
        builder.SetBasePath(AppContext.BaseDirectory);
        builder.AddJsonFile("appsettings.json", optional: false);
        builder.AddJsonFile("appsettings.Development.json", optional: true);
        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    private static string? ResolveConnectionString()
    {
        var configuration = LoadConfiguration();
        var connectionString = configuration.GetConnectionString("Kgi");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            if (!string.Equals(databaseName, "KGI", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("本版本僅使用 KGI，請將 ConnectionStrings:Kgi 的 Database 設為 KGI。");
            }
        }

        return connectionString;
    }

    private static bool ResolveRunDatabaseTests()
    {
        var value = LoadConfiguration()["KGI_RUN_DB_TESTS"];
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
