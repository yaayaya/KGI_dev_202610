using Microsoft.Data.SqlClient;

namespace Kgi.Api.Data;

/// <summary>
/// 透過 .NET 標準設定讀取共用的 Kgi 連線字串，每次建立獨立連線。
/// </summary>
public sealed class SqlConnectionFactory(IConfiguration configuration)
{
    /// <summary>
    /// 建立尚未開啟的連線，呼叫端負責非同步開啟及釋放；未設定連線字串時明確回報錯誤。
    /// </summary>
    public SqlConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString("Kgi");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("請在 Kgi.Api/appsettings.json 設定 ConnectionStrings:Kgi 資料庫連線。");
        }

        return new SqlConnection(connectionString);
    }
}
