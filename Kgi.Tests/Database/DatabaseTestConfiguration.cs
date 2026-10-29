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
    /// 判斷資料庫測試是否具備執行條件。
    /// </summary>
    public static bool IsEnabled => RunDatabaseTests && !string.IsNullOrWhiteSpace(ConnectionString);

    private static IConfiguration LoadConfiguration()
    {
        var builder = new ConfigurationBuilder();
        builder.SetBasePath(AppContext.BaseDirectory);
        builder.AddJsonFile("appsettings.json", optional: true);
        builder.AddJsonFile("appsettings.Development.json", optional: true);

        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null)
        {
            var apiDevJson = Path.Combine(currentDir.FullName, "Kgi.Api", "appsettings.Development.json");
            if (File.Exists(apiDevJson))
            {
                builder.AddJsonFile(apiDevJson, optional: true);
                break;
            }

            var rootDevJson = Path.Combine(currentDir.FullName, "appsettings.Development.json");
            if (File.Exists(rootDevJson))
            {
                builder.AddJsonFile(rootDevJson, optional: true);
                break;
            }

            currentDir = currentDir.Parent;
        }

        // 與 API 共用同一個 UserSecretsId，環境變數仍具有最高優先序。
        builder.AddUserSecrets(typeof(Program).Assembly, optional: true);
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
