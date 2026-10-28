import Papa from 'papaparse';
import type { ParseMeta } from 'papaparse';
import { onScopeDispose, ref, shallowRef, watch } from 'vue';
import type { Ref } from 'vue';

/**
 * 瀏覽器預覽專用的原始欄位；這不是 API DTO，價格及代碼保留 CSV 字串。
 */
interface PreviewRow {
  Stock_ID: string;
  Closing_Price: string;
  Trade_Date: string;
}

/**
 * 點開彈窗才解析前十筆，完整檔案驗證仍由後端執行；換檔與卸載會失效舊讀取。
 */
export function useCsvPreview(file: Readonly<Ref<File | null>>) {
  const open = ref(false);
  const loading = ref(false);
  const error = ref('');
  const rows = shallowRef<PreviewRow[]>([]);
  let version = 0;
  let active = true;
  const requiredHeaders = ['Stock_ID', 'Closing_Price', 'Trade_Date'];

  /**
   * 使用主執行緒讀取小量預覽，避開目前 Vite 8 與 Papa Parse Worker 的相容性回報。
   */
  function showPreview() {
    const selected = file.value;
    if (!selected || !active) return;
    open.value = true;
    if (rows.value.length || error.value || loading.value) return;
    const requestVersion = ++version;
    loading.value = true;
    Papa.parse<PreviewRow>(selected, {
      header: true,
      delimiter: ',',
      dynamicTyping: false,
      preview: 10,
      worker: false,
      encoding: 'UTF-8',
      skipEmptyLines: 'greedy',
      transformHeader: (header) => header.trim(),
      complete(result) {
        if (!active || requestVersion !== version || file.value !== selected) return;
        loading.value = false;
        const meta = result.meta as ParseMeta & { renamedHeaders?: Record<string, string> };
        const headers = meta.fields ?? [];
        if (Object.keys(meta.renamedHeaders ?? {}).length) {
          error.value = 'CSV 表頭含有重複欄位，請修正後重新選擇。';
        } else if (
          headers.length !== requiredHeaders.length ||
          requiredHeaders.some((name, index) => headers[index] !== name)
        ) {
          error.value = 'CSV 表頭須依序為 Stock_ID、Closing_Price、Trade_Date。';
        } else if (result.errors.length) {
          const first = result.errors[0]!;
          const location = first.row === undefined ? '' : `第 ${first.row + 1} 筆資料附近：`;
          error.value = `${location}CSV 格式不完整，請檢查欄位數及引號。`;
        } else if (!result.data.length) {
          error.value = 'CSV 只有表頭，沒有可預覽的行情資料。';
        } else {
          rows.value = result.data;
        }
      },
      error() {
        if (!active || requestVersion !== version || file.value !== selected) return;
        loading.value = false;
        error.value = '無法讀取檔案，請重新選擇。';
      },
    });
  }

  // 不把前一個檔案的預覽或錯誤帶入新檔案，也不強迫選檔後開啟彈窗。
  watch(open, (opened) => {
    if (opened) showPreview();
  });

  watch(
    file,
    () => {
      version += 1;
      open.value = false;
      loading.value = false;
      rows.value = [];
      error.value = '';
    },
    { flush: 'sync' },
  );

  onScopeDispose(() => {
    active = false;
    version += 1;
  });

  return { open, loading, error, rows, showPreview };
}
