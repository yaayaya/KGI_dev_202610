namespace Kgi.Api.Features.Interest;

/// <summary>
/// 利息批次的參數化 SQL；批次寫入、鎖定及總額校驗均集中於此。
/// </summary>
public static class InterestSql
{
    private const string BatchColumns = """
        Batch_ID AS BatchId, Expected_Amt AS ExpectedAmt,
        Process_Status AS ProcessStatus, Progress_Rate AS ProgressRate,
        Started_Time AS StartedTime, Finished_Time AS FinishedTime, Error_Message AS ErrorMessage
        """;

    public const string Batches = "SELECT " + BatchColumns + " FROM dbo.Interest_Batch ORDER BY Batch_ID DESC;";

    public const string Batch = "SELECT " + BatchColumns + " FROM dbo.Interest_Batch WHERE Batch_ID = @BatchId;";

    public const string Details = """
        SELECT Detail_ID AS DetailId, Batch_ID AS BatchId,
               Contract_No AS ContractNo, Daily_Interest AS DailyInterest
        FROM dbo.Interest_Detail WHERE Batch_ID = @BatchId ORDER BY Contract_No, Detail_ID;
        """;

    public const string Accept = """
        UPDATE dbo.Interest_Batch
        SET Process_Status = 1, Progress_Rate = 0, Started_Time = NULL,
            Finished_Time = NULL, Error_Message = NULL
        OUTPUT INSERTED.Batch_ID AS BatchId, INSERTED.Expected_Amt AS ExpectedAmt,
               INSERTED.Process_Status AS ProcessStatus, INSERTED.Progress_Rate AS ProgressRate,
               INSERTED.Started_Time AS StartedTime, INSERTED.Finished_Time AS FinishedTime,
               INSERTED.Error_Message AS ErrorMessage
        WHERE Batch_ID = @BatchId AND Process_Status = 0;
        """;

    public const string Pending = """
        SELECT TOP (100) Batch_ID FROM dbo.Interest_Batch
        WHERE Process_Status = 1 ORDER BY Batch_ID;
        """;

    public const string AcquireLock = """
        DECLARE @Result INT;
        EXEC @Result = sys.sp_getapplock @Resource = @LockResource,
            @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
        SELECT @Result;
        """;

    public const string ReleaseLock = """
        EXEC sys.sp_releaseapplock @Resource = @LockResource, @LockOwner = 'Session';
        """;

    public const string Start = """
        UPDATE dbo.Interest_Batch
        SET Started_Time = SYSUTCDATETIME(), Progress_Rate = 1
        WHERE Batch_ID = @BatchId AND Process_Status = 1 AND Started_Time IS NULL;
        """;

    public const string Sources = """
        SELECT Batch_ID AS BatchId, Contract_No AS ContractNo,
               Principal_Amt AS PrincipalAmt, Annual_Rate AS AnnualRate
        FROM dbo.Interest_Source WHERE Batch_ID = @BatchId ORDER BY Contract_No;
        """;

    public const string InsertDetails = """
        INSERT INTO dbo.Interest_Detail (Batch_ID, Contract_No, Daily_Interest)
        SELECT @BatchId, D.ContractNo, D.DailyInterest
        FROM OPENJSON(@Details)
        WITH
        (
            ContractNo VARCHAR(20) '$.contractNo',
            DailyInterest DECIMAL(18, 4) '$.dailyInterest'
        ) AS D;
        """;

    public const string Progress = """
        UPDATE dbo.Interest_Batch
        SET Progress_Rate = @ProgressRate
        WHERE Batch_ID = @BatchId AND Process_Status = 1
          AND Started_Time IS NOT NULL AND Progress_Rate < @ProgressRate;
        """;

    public const string CheckAmounts = """
        -- SUM 會提升為 38,4；先轉成 28,4，避免相減時縮減小數位而吞掉 0.0001 邊界。
        DECLARE @Actual DECIMAL(28, 4) =
            COALESCE((SELECT SUM(Daily_Interest) FROM dbo.Interest_Detail WHERE Batch_ID = @BatchId), 0);
        SELECT @Actual AS ActualAmt, Expected_Amt AS ExpectedAmt,
               CAST(CASE WHEN ABS(@Actual - Expected_Amt) <= CAST(0.0001 AS DECIMAL(5, 4))
                         THEN 1 ELSE 0 END AS BIT) AS IsConsistent
        FROM dbo.Interest_Batch WITH (UPDLOCK, HOLDLOCK) WHERE Batch_ID = @BatchId;
        """;

    public const string Complete = """
        UPDATE dbo.Interest_Batch
        SET Process_Status = 2, Progress_Rate = 100, Finished_Time = SYSUTCDATETIME(), Error_Message = NULL
        WHERE Batch_ID = @BatchId AND Process_Status = 1 AND Started_Time IS NOT NULL;
        """;

    public const string Fail = """
        -- 提交結果不明時，也不可將已成功的 2 覆寫成失敗。
        UPDATE dbo.Interest_Batch
        SET Process_Status = 9, Finished_Time = SYSUTCDATETIME(), Error_Message = @ErrorMessage
        WHERE Batch_ID = @BatchId AND Process_Status = 1;
        """;
}
