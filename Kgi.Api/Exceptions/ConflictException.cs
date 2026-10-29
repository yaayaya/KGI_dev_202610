namespace Kgi.Api.Exceptions;

/// <summary>
/// 表示請求識別碼已被不同操作內容使用，無法安全重送。
/// </summary>
public sealed class ConflictException(string message) : Exception(message);
