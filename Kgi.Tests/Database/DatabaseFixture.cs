using Kgi.Tests.Database.Scripts;

namespace Kgi.Tests.Database;

[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public string ConnectionString => Environment.GetEnvironmentVariable("KGI_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("請設定 KGI_TEST_CONNECTION_STRING，並指定 KGI_Test 資料庫。");

    public async Task InitializeAsync()
    {
        if (DatabaseFactAttribute.IsEnabled)
        {
            await InitTestDbTool.RebuildAsync(ConnectionString);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class DatabaseFactAttribute : FactAttribute
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("KGI_ALLOW_DB_REBUILD") == "1"
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KGI_TEST_CONNECTION_STRING"));

    public DatabaseFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = "資料庫測試需設定 KGI_ALLOW_DB_REBUILD=1 與 KGI_TEST_CONNECTION_STRING。";
        }
    }
}
