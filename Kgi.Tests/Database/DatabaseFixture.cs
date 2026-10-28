using Kgi.Tests.Database.Scripts;

namespace Kgi.Tests.Database;

[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public string ConnectionString => DatabaseTestConfiguration.ConnectionString
        ?? throw new InvalidOperationException("請設定 KGI_TEST_CONNECTION_STRING，或於 appsettings.Development.json 設定 ConnectionStrings:KgiTest。");

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
    public static bool IsEnabled => DatabaseTestConfiguration.IsEnabled;

    public DatabaseFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = "資料庫測試需設定 KGI_ALLOW_DB_REBUILD=1 與 KGI_TEST_CONNECTION_STRING（可於 appsettings.Development.json 設定）。";
        }
    }
}
