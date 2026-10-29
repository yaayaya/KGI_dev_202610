using Kgi.Tests.Database.Scripts;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database;

/// <summary>
/// 不連線資料庫，驗證重建入口只能操作共用的 KGI。
/// </summary>
public sealed class InitTestDbToolTests
{
    /// <summary>
    /// KGI 名稱大小寫不影響重建目標驗證。
    /// </summary>
    [Theory]
    [InlineData("KGI")]
    [InlineData("kgi")]
    [InlineData("KgI")]
    public void ValidateDatabaseNameAcceptsKgi(string databaseName)
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "127.0.0.1",
            InitialCatalog = databaseName
        }.ConnectionString;

        Assert.Equal(databaseName, InitTestDbTool.ValidateDatabaseName(connectionString));
    }

    /// <summary>
    /// 未指定資料庫或指定其他資料庫時，須在連線及刪表前拒絕重建。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("master")]
    [InlineData("tempdb")]
    [InlineData("OtherDatabase")]
    [InlineData("KGI_Archive")]
    [InlineData("KGI_Test")]
    [InlineData(" KGI ")]
    public void ValidateDatabaseNameRejectsOtherDatabases(string databaseName)
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "127.0.0.1",
            InitialCatalog = databaseName
        }.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => InitTestDbTool.ValidateDatabaseName(connectionString));
    }
}
