using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database.Scripts;

public static class InitTestDbTool
{
    public static string ValidateDatabaseName(string connectionString)
    {
        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!string.Equals(name, "KGI_Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("測試初始化只允許精確指定 KGI_Test 資料庫，大小寫不拘。");
        }

        return name;
    }

    public static async Task RebuildAsync(string connectionString)
    {
        if (Environment.GetEnvironmentVariable("KGI_ALLOW_DB_REBUILD") != "1")
        {
            throw new InvalidOperationException("請明確設定 KGI_ALLOW_DB_REBUILD=1 才能執行重建。");
        }

        ValidateDatabaseName(connectionString);
        var root = Path.Combine(AppContext.BaseDirectory, "Database", "Scripts", "InitTestDb");
        // 清表前讀完三檔，避免重建途中才發現缺少索引或預設資料腳本。
        var createSql = await File.ReadAllTextAsync(Path.Combine(root, "DbScript", "BuildInitDbScript.sql"));
        var indexSql = await File.ReadAllTextAsync(Path.Combine(root, "DbScript", "BuildIndexScript.sql"));
        var preloadSql = await File.ReadAllTextAsync(Path.Combine(root, "Preload.sql"));

        var target = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "KGI_Test" };
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using (var masterConnection = new SqlConnection(master.ConnectionString))
        {
            await masterConnection.OpenAsync();
            await masterConnection.ExecuteAsync(new CommandDefinition(
                "IF DB_ID(N'KGI_Test') IS NULL CREATE DATABASE [KGI_Test];", commandTimeout: 60));
        }

        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await ExecuteScriptAsync(connection, createSql);
        await ExecuteScriptAsync(connection, indexSql);
        await ExecuteScriptAsync(connection, preloadSql);
    }

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
