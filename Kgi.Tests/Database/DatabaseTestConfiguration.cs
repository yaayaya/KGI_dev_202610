using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Database;

/// <summary>
/// 集中讀取資料庫測試設定，依序支援環境變數與 appsettings.Development.json。
/// </summary>
public static class DatabaseTestConfiguration
{
    private static readonly Lazy<string?> LazyConnectionString = new(ResolveConnectionString);
    private static readonly Lazy<bool> LazyAllowDbRebuild = new(ResolveAllowDbRebuild);

    /// <summary>
    /// 取得 KGI_Test 連線字串；若尚未設定環境變數，會從設定檔載入並回填環境變數。
    /// </summary>
    public static string? ConnectionString => LazyConnectionString.Value;

    /// <summary>
    /// 判斷是否允許重建測試資料庫；若由設定檔啟用，會同步回填環境變數。
    /// </summary>
    public static bool AllowDbRebuild => LazyAllowDbRebuild.Value;

    /// <summary>
    /// 判斷資料庫測試是否具備執行條件。
    /// </summary>
    public static bool IsEnabled => AllowDbRebuild && !string.IsNullOrWhiteSpace(ConnectionString);

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

        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    private static string? ResolveConnectionString()
    {
        var envValue = Environment.GetEnvironmentVariable("KGI_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        var configuration = LoadConfiguration();
        var connectionString = configuration.GetConnectionString("KgiTest")
            ?? configuration.GetConnectionString("KGI_TEST_CONNECTION_STRING")
            ?? configuration["KGI_TEST_CONNECTION_STRING"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var defaultConnection = configuration.GetConnectionString("Kgi");
            if (!string.IsNullOrWhiteSpace(defaultConnection))
            {
                var builder = new SqlConnectionStringBuilder(defaultConnection)
                {
                    InitialCatalog = "KGI_Test"
                };
                connectionString = builder.ConnectionString;
            }
        }

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            Environment.SetEnvironmentVariable("KGI_TEST_CONNECTION_STRING", connectionString);
        }

        return connectionString;
    }

    private static bool ResolveAllowDbRebuild()
    {
        var envValue = Environment.GetEnvironmentVariable("KGI_ALLOW_DB_REBUILD");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue == "1" || envValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        var configuration = LoadConfiguration();
        var value = configuration["KGI_ALLOW_DB_REBUILD"]
            ?? configuration["AllowDbRebuild"];

        var isAllowed = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        if (isAllowed)
        {
            Environment.SetEnvironmentVariable("KGI_ALLOW_DB_REBUILD", "1");
        }

        return isAllowed;
    }
}
