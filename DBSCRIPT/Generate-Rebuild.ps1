# 依三份手寫 SQL 來源重產 SSMS 用完整重建檔；本工具不連線或修改資料庫。
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceRoot = Join-Path $repositoryRoot 'Kgi.Tests/Database/Scripts/InitTestDb'
$sourceFiles = @('DbScript/BuildInitDbScript.sql', 'DbScript/BuildIndexScript.sql', 'Preload.sql')
$output = [Collections.Generic.List[string]]::new()

$header = @'
-- 此檔由 DBSCRIPT/Generate-Rebuild.ps1 產生，請勿直接修改。
-- 唯一手寫來源：Kgi.Tests/Database/Scripts/InitTestDb/ 的建表、索引及預載三檔。
-- KGI 完整重建：清除並重建 Q1／Q2／Q3／Q4 共十八張資料表、索引、備註及假資料。
-- 在 SSMS 整份執行即可，不需 SQLCMD 模式或其他檔案；只操作 KGI。
-- 建庫必須在交易外；後續失敗會回滾資料表與資料，新建的空資料庫仍會保留。
USE [master];
GO
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR (N'請先結束目前的交易，再執行 KGI 完整重建。', 16, 1);
    RETURN;
END;
IF DB_ID(N'KGI') IS NULL
    EXEC (N'CREATE DATABASE [KGI];');
GO
USE [KGI];
GO
-- 再次防護外層交易；所有內容仍明確指定 KGI，避免前一批次錯誤後誤改其他資料庫。
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR (N'請先結束目前的交易，再執行 KGI 完整重建。', 16, 1);
    RETURN;
END;
IF DB_NAME() <> N'KGI'
BEGIN
    RAISERROR (N'目前連線不是 KGI，拒絕重建。', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET IMPLICIT_TRANSACTIONS OFF;

BEGIN TRY
    BEGIN TRANSACTION;
'@
$output.Add($header.TrimEnd())

foreach ($relativePath in $sourceFiles) {
    $source = [IO.File]::ReadAllText((Join-Path $sourceRoot $relativePath), [Text.Encoding]::UTF8)
    $source = $source.Replace("`r`n", "`n")
    # 由外層統一管理交易，保留原本 GO 的編譯邊界與所有條件、固定 VALUES。
    $source = [regex]::Replace($source,
        '(?im)^[\t ]*(?:SET XACT_ABORT ON;|BEGIN TRANSACTION;|COMMIT TRANSACTION;)[\t ]*\r?$', '')
    $output.Add('    -- 來源：Kgi.Tests/Database/Scripts/InitTestDb/' + $relativePath)
    foreach ($batch in [regex]::Split($source, '(?im)^[\t ]*GO[\t ]*(?:--[^\r\n]*)?\r?$')) {
        if (-not [string]::IsNullOrWhiteSpace($batch)) {
            $escapedBatch = $batch.Trim().Replace("'", "''")
            $output.Add("    EXEC [KGI].sys.sp_executesql N'" + $escapedBatch + "';")
        }
    }
}

$footer = @'
    -- 提交前核對全部功能的初始數量與狀態，詳細逐戶控制由 RebuildDbTests 驗證。
    EXEC [KGI].sys.sp_executesql N'
IF (SELECT COUNT(*) FROM dbo.Warrant_Master) <> 800
    OR (SELECT COUNT(*) FROM dbo.Warrant_Trial_Log) <> 0
    OR (SELECT COUNT(*) FROM dbo.Lending_Contract) <> 1000
    OR (SELECT COUNT(*) FROM dbo.Market_Price) <> 0
    OR (SELECT COUNT(*) FROM dbo.Interest_Batch) <> 3
    OR (SELECT COUNT(*) FROM dbo.Interest_Source) <> 1015
    OR (SELECT COUNT(*) FROM dbo.Interest_Detail) <> 0
    OR (SELECT COUNT(*) FROM dbo.Interest_Batch WHERE Process_Status = 0 AND Progress_Rate = 0
        AND Started_Time IS NULL AND Finished_Time IS NULL AND Error_Message IS NULL) <> 3
    OR (SELECT COUNT(*) FROM dbo.Company_Master) <> 3
    OR (SELECT COUNT(*) FROM dbo.Shareholder_Master) <> 300
    OR (SELECT COUNT(*) FROM dbo.Shareholding_Register) <> 506
    OR (SELECT COUNT(*) FROM dbo.Shareholding_Register WHERE Share_Qty > 0) <> 500
    OR (SELECT COUNT(*) FROM dbo.Shareholding_Snapshot) <> 6
    OR (SELECT COUNT(*) FROM dbo.Shareholding_Snapshot_Detail) <> 1000
    OR (SELECT COUNT(*) FROM dbo.Share_Operation_Request) <> 1
    OR (SELECT COUNT(*) FROM dbo.Share_Transfer_Log) <> 1
    OR (SELECT COUNT(*) FROM dbo.Shareholders_Meeting WHERE Notice_Sent_Flag = 0) <> 6
    OR (SELECT COUNT(*) FROM dbo.Shareholders_Meeting) <> 6
    OR (SELECT COUNT(*) FROM dbo.Dividend_Batch WHERE Status = 0
        AND Completed_Time IS NULL AND Operator_ID IS NULL) <> 8
    OR (SELECT COUNT(*) FROM dbo.Dividend_Batch) <> 8
    OR (SELECT COUNT(*) FROM dbo.Meeting_Notice_Detail) <> 0
    OR (SELECT COUNT(*) FROM dbo.Dividend_Detail) <> 0
    THROW 51001, N''KGI 預載筆數或初始狀態不符，重建已取消。'', 1;

IF (SELECT SUM(CAST(Share_Qty AS BIGINT)) FROM dbo.Shareholding_Register WHERE Company_Code = ''DEMO01'') <> 235257700
    OR (SELECT SUM(CAST(Pledge_Qty AS BIGINT)) FROM dbo.Shareholding_Register WHERE Company_Code = ''DEMO01'') <> 90214835
    OR (SELECT SUM(CAST(Share_Qty AS BIGINT)) FROM dbo.Shareholding_Register WHERE Company_Code = ''DEMO02'') <> 62018900
    OR (SELECT SUM(CAST(Pledge_Qty AS BIGINT)) FROM dbo.Shareholding_Register WHERE Company_Code = ''DEMO02'') <> 22935495
    OR (SELECT SUM(CAST(Share_Qty AS BIGINT)) FROM dbo.Shareholding_Register WHERE Company_Code = ''DEMO03'') <> 11322
    OR (SELECT SUM(CAST(Pledge_Qty AS BIGINT)) FROM dbo.Shareholding_Register WHERE Company_Code = ''DEMO03'') <> 1366
    THROW 51001, N''KGI 股務預載控制總額不符，重建已取消。'', 1;

SELECT N''Warrant_Master'' AS [資料表], COUNT(*) AS [筆數] FROM dbo.Warrant_Master
UNION ALL SELECT N''Warrant_Trial_Log'', COUNT(*) FROM dbo.Warrant_Trial_Log
UNION ALL SELECT N''Lending_Contract'', COUNT(*) FROM dbo.Lending_Contract
UNION ALL SELECT N''Market_Price'', COUNT(*) FROM dbo.Market_Price
UNION ALL SELECT N''Interest_Batch'', COUNT(*) FROM dbo.Interest_Batch
UNION ALL SELECT N''Interest_Source'', COUNT(*) FROM dbo.Interest_Source
UNION ALL SELECT N''Interest_Detail'', COUNT(*) FROM dbo.Interest_Detail
UNION ALL SELECT N''Company_Master'', COUNT(*) FROM dbo.Company_Master
UNION ALL SELECT N''Shareholder_Master'', COUNT(*) FROM dbo.Shareholder_Master
UNION ALL SELECT N''Shareholding_Register'', COUNT(*) FROM dbo.Shareholding_Register
UNION ALL SELECT N''Shareholding_Snapshot'', COUNT(*) FROM dbo.Shareholding_Snapshot
UNION ALL SELECT N''Shareholding_Snapshot_Detail'', COUNT(*) FROM dbo.Shareholding_Snapshot_Detail
UNION ALL SELECT N''Share_Operation_Request'', COUNT(*) FROM dbo.Share_Operation_Request
UNION ALL SELECT N''Share_Transfer_Log'', COUNT(*) FROM dbo.Share_Transfer_Log
UNION ALL SELECT N''Shareholders_Meeting'', COUNT(*) FROM dbo.Shareholders_Meeting
UNION ALL SELECT N''Dividend_Batch'', COUNT(*) FROM dbo.Dividend_Batch
UNION ALL SELECT N''Meeting_Notice_Detail'', COUNT(*) FROM dbo.Meeting_Notice_Detail
UNION ALL SELECT N''Dividend_Detail'', COUNT(*) FROM dbo.Dividend_Detail;';

    COMMIT TRANSACTION;
    PRINT N'KGI 完整重建與預載完成。';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
'@
$output.Add($footer.TrimEnd())
$targetPath = Join-Path $PSScriptRoot 'Rebuild.sql'
[IO.File]::WriteAllText($targetPath, ($output -join "`n`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output '已依三份來源重產 DBSCRIPT/Rebuild.sql；未連線資料庫。'
