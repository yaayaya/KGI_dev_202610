using Microsoft.Data.SqlClient;

namespace Kgi.Api.Data;

/// <summary>
/// 從 User Secrets 或環境設定建立獨立的 SQL Server 連線，不共用可變連線狀態。
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
            throw new InvalidOperationException("請透過 User Secrets 或 ConnectionStrings__Kgi 設定資料庫連線。");
        }

        return new SqlConnection(connectionString);
    }
}
