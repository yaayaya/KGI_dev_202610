import type { AxiosError, AxiosRequestConfig } from 'axios';
import { http } from './http';

/**
 * Orval 的手寫請求入口：沿用共用 Axios，保留取消訊號與 HTTP 錯誤，僅取出回應資料。
 */
export async function request<T>(
  config: AxiosRequestConfig,
  options?: AxiosRequestConfig,
): Promise<T> {
  const response = await http.request<T>({ ...config, ...options });
  return response.data;
}

/**
 * 提供產碼工具引用的錯誤型別，保留後端狀態碼及 ProblemDetails 內容。
 */
export type ErrorType<T> = AxiosError<T>;
