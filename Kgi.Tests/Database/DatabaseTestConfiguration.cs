using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Database;

/// <summary>
/// 資料庫測試沿用 API 的 Kgi 連線，設定連線後即可執行；只有重建須另外啟用。
/// </summary>
public static class DatabaseTestConfiguration
{
    private static readonly Lazy<string?> LazyConnectionString = new(ResolveConnectionString);

    /// <summary>
    /// 沿用 API 的 ConnectionStrings:Kgi，不另行切換資料庫或維護測試連線。
    /// </summary>
    public static string? ConnectionString => LazyConnectionString.Value;

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
    /// 連線設定完成即可執行一般資料庫測試，不需要額外旗標。
    /// </summary>
    public static bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);

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
}
