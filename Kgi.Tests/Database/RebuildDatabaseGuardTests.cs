using Kgi.Tests.Database.Scripts;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database;

/// <summary>
/// 不連線資料庫，驗證重建只接受唯一 KGI 且不默默改寫其他目標。
/// </summary>
public sealed class RebuildDatabaseGuardTests
{
    /// <summary>
    /// KGI 名稱大小寫不影響識別，驗證後仍保留原本名稱。
    /// </summary>
    [Theory]
    [InlineData("KGI")]
    [InlineData("kgi")]
    [InlineData("KgI")]
    public void AcceptsOnlyKgiWithoutRewritingName(string databaseName)
    {
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = "localhost",
            InitialCatalog = databaseName
        };

        Assert.Equal(databaseName, InitTestDbTool.ValidateDatabaseName(connection.ConnectionString));
    }

    /// <summary>
    /// 系統資料庫、相似名稱、舊分流名稱與未指定資料庫均須在連線前拒絕。
    /// </summary>
    [Theory]
    [InlineData("master")]
    [InlineData("KGI_Test")]
    [InlineData("KGI_Archive")]
    [InlineData("Other")]
    [InlineData("")]
    [InlineData(" KGI ")]
    public void RejectsEveryOtherDatabaseBeforeOpeningConnection(string databaseName)
    {
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = "localhost",
            InitialCatalog = databaseName
        };

        Assert.Throws<InvalidOperationException>(() => InitTestDbTool.ValidateDatabaseName(connection.ConnectionString));
    }
}
