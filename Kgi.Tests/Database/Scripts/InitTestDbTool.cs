using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database.Scripts;

/// <summary>
/// 依明確開關與資料庫名稱防護，重建 API 共用的 KGI 資料表、索引及假資料。
/// </summary>
public static class InitTestDbTool
{
    /// <summary>
    /// 只接受精確指定 KGI 的連線，不改寫或猜測目標資料庫名稱。
    /// </summary>
    public static string ValidateDatabaseName(string connectionString)
    {
        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!string.Equals(name, "KGI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("資料初始化只允許精確指定 KGI 資料庫，大小寫不拘；其他資料庫一律拒絕。");
        }

        return name;
    }

    /// <summary>
    /// 在 KGI 不存在時建立，再確切重建其 Q1、Q2、Q3、Q4 表、索引與預載資料。
    /// </summary>
    public static async Task RebuildAsync(string connectionString)
    {
        if (!DatabaseTestConfiguration.AllowDbRebuild)
        {
            throw new InvalidOperationException("此操作會清除並重建 KGI，請明確設定環境變數 KGI_ALLOW_DB_REBUILD=1。");
        }

        ValidateDatabaseName(connectionString);
        var root = Path.Combine(AppContext.BaseDirectory, "Database", "Scripts", "InitTestDb");
        // 清表前讀完三檔，避免重建途中才發現缺少索引或預設資料腳本。
        var createSql = await File.ReadAllTextAsync(Path.Combine(root, "DbScript", "BuildInitDbScript.sql"));
        var indexSql = await File.ReadAllTextAsync(Path.Combine(root, "DbScript", "BuildIndexScript.sql"));
        var preloadSql = await File.ReadAllTextAsync(Path.Combine(root, "Preload.sql"));

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using (var masterConnection = new SqlConnection(master.ConnectionString))
        {
            await masterConnection.OpenAsync();
            await masterConnection.ExecuteAsync(new CommandDefinition(
                "IF DB_ID(N'KGI') IS NULL CREATE DATABASE [KGI];", commandTimeout: 60));
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        // 實際連線後再次核對，確保清表腳本確切作用於唯一的 KGI。
        var actualDatabase = await connection.ExecuteScalarAsync<string>(new CommandDefinition("SELECT DB_NAME();"));
        if (!string.Equals(actualDatabase, "KGI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("實際連線目標不是 KGI，拒絕執行重建。");
        }

        await ExecuteScriptAsync(connection, createSql);
        await ExecuteScriptAsync(connection, indexSql);
        await ExecuteScriptAsync(connection, preloadSql);
    }

    /// <summary>
    /// 在同一連線依序執行 SQL 腳本各批次，保留跨 GO 的明確交易。
    /// </summary>
    private static async Task ExecuteScriptAsync(SqlConnection connection, string sql)
    {
        // SqlClient 不識別 GO，僅處理腳本中獨立一行的批次分隔符。
        foreach (var batch in Regex.Split(sql, @"^[\t ]*GO[\t ]*(?:--[^\r\n]*)?\r?$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(batch, commandTimeout: 60));
        }
    }
}
