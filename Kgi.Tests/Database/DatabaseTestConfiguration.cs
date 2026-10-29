using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Database;

/// <summary>
/// 與本機 API 共用 KGI 連線，依序讀取設定檔、User Secrets 及環境變數。
/// </summary>
public static class DatabaseTestConfiguration
{
    private static readonly Lazy<IConfiguration> Configuration = new(LoadConfiguration);

    /// <summary>
    /// 直接取得 API 使用的 ConnectionStrings:Kgi，不改寫資料庫名稱或環境變數。
    /// </summary>
    public static string? ConnectionString => Configuration.Value.GetConnectionString("Kgi");

    /// <summary>
    /// 只有明確設定環境變數 KGI_ALLOW_DB_REBUILD=1 才允許清除並重建 KGI。
    /// </summary>
    public static bool AllowDbRebuild => Environment.GetEnvironmentVariable("KGI_ALLOW_DB_REBUILD") == "1";

    /// <summary>
    /// 判斷資料庫測試是否具備執行條件。
    /// </summary>
    public static bool IsEnabled => AllowDbRebuild && !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>
    /// 載入本機開發 API 的同一組設定來源，後加入的來源優先。
    /// </summary>
    private static IConfiguration LoadConfiguration() => new ConfigurationBuilder()
        .SetBasePath(FindApiConfigurationDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddUserSecrets(typeof(Program).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>
    /// 優先使用原始 API 設定目錄，獨立測試產物則使用複製至輸出目錄的設定。
    /// </summary>
    private static string FindApiConfigurationDirectory()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory != null)
        {
            var apiDirectory = Path.Combine(currentDirectory.FullName, "Kgi.Api");
            if (File.Exists(Path.Combine(apiDirectory, "Kgi.Api.csproj")))
            {
                return apiDirectory;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
