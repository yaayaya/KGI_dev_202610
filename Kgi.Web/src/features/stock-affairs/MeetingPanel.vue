<script setup lang="ts">
import { computed, ref } from 'vue';
import { LoaderCircle, Send } from '@lucide/vue';
import type { StockMeetingResponse, StockNoticesResponse } from '@/api/generated/kgiApi.schemas';
import { Button } from '@/components/ui/button';
import HelpHint from '@/components/HelpHint.vue';
import { formatStockTime } from './stockAffairsApi';

/**
 * 會議清單與不可變基準日通知名單，所有收件人及通知狀態均來自 API。
 */
const props = defineProps<{
  meetings: StockMeetingResponse[];
  notices: StockNoticesResponse | null;
  loading: boolean;
  detailLoading: boolean;
  detailError: string;
  busy: boolean;
}>();
const emit = defineEmits<{ preview: [meetingId: string]; send: [meetingId: string] }>();
const requestedId = ref('');
const selectedMeeting = computed(() =>
  props.meetings.find((row) => row.meetingId === (props.notices?.meetingId || requestedId.value)),
);

/**
 * 記住此次選擇供讀取失敗後重試，內容交由父層取消舊請求。
 */
function preview(id: string) {
  requestedId.value = id;
  emit('preview', id);
}
</script>

<template>
  <div class="flex h-full min-h-0 flex-col gap-2 overflow-hidden">
    <!-- 會議日期與基準日並列，通知狀態以文字和顏色共同呈現。 -->
    <section
      class="erp-panel flex max-h-[42%] min-h-0 shrink-0 flex-col overflow-hidden"
      aria-labelledby="stock-meetings-title"
      :aria-busy="loading"
    >
      <div class="erp-panel-heading shrink-0">
        <h2 id="stock-meetings-title" class="flex items-center gap-1">
          股東會管理<HelpHint label="會議基準日與通知說明"
            >基準日為開會日前 30 個日曆日，通知名單使用該日的姓名、地址與持股。此為模擬寄發，不會實際寄信。</HelpHint
          >
        </h2>
      </div>
      <div class="min-h-0 overflow-auto">
        <table class="erp-table min-w-[700px]">
          <thead class="sticky top-0 z-10">
            <tr>
              <th class="text-left!">會議編號</th>
              <th class="text-left!">類型</th>
              <th>開會日期</th>
              <th>基準日</th>
              <th>股東人數</th>
              <th>通知狀態</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="meeting in meetings"
              :key="meeting.meetingId"
              :class="notices?.meetingId === meeting.meetingId ? 'bg-accent/50' : ''"
            >
              <td class="text-left! font-medium">{{ meeting.meetingId }}</td>
              <td class="text-left!">
                {{ meeting.meetingType === 'AGM' ? '股東常會' : '股東臨時會' }}
              </td>
              <td>{{ meeting.meetingDate }}</td>
              <td>{{ meeting.recordDate }}</td>
              <td>{{ meeting.recipientCount.toLocaleString('zh-TW') }}</td>
              <td>
                <span :class="meeting.noticeSent ? 'text-success' : 'text-muted-foreground'">{{
                  meeting.noticeSent ? '已通知' : '未通知'
                }}</span>
              </td>
              <td>
                <Button
                  type="button"
                  variant="outline"
                  size="xs"
                  :disabled="busy || loading"
                  @click="preview(meeting.meetingId)"
                  >{{ meeting.noticeSent ? '查看通知紀錄' : '預覽通知名單' }}</Button
                >
              </td>
            </tr>
            <tr v-if="!meetings.length">
              <td colspan="7" class="h-20! text-center! text-muted-foreground">
                {{ loading ? '載入會議中…' : '尚無股東會' }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- 預覽不變更通知旗標；確認後保存收件資料及模擬通知時間。 -->
    <section
      class="erp-panel flex min-h-0 flex-1 flex-col overflow-hidden"
      aria-labelledby="stock-notice-title"
      :aria-busy="detailLoading"
    >
      <div class="erp-panel-heading shrink-0 gap-2 py-1.5">
        <h2 id="stock-notice-title" class="min-w-0 truncate">
          {{ notices?.noticeSent ? '已保存通知明細' : '基準日通知預覽' }}
        </h2>
        <Button
          v-if="notices && !notices.noticeSent"
          type="button"
          size="sm"
          class="shrink-0"
          :disabled="busy || detailLoading || !notices.recipients.length"
          @click="emit('send', notices.meetingId)"
          ><LoaderCircle v-if="busy" class="size-3.5 animate-spin" /><Send
            v-else
            class="size-3.5"
          />{{ busy ? '處理中…' : '產生並寄發通知' }}</Button
        >
        <span v-else-if="notices?.noticeSent" class="shrink-0 text-xs font-medium text-success"
          >通知已完成</span
        >
      </div>
      <div
        v-if="detailError"
        role="alert"
        class="flex h-12 shrink-0 items-center justify-between gap-2 border-b bg-danger-muted px-3 text-xs text-destructive"
      >
        <p class="line-clamp-2 min-w-0 flex-1 leading-4" :title="detailError">{{ detailError }}</p>
        <Button
          type="button"
          variant="outline"
          size="xs"
          class="shrink-0"
          :disabled="busy || detailLoading || !requestedId"
          @click="preview(requestedId)"
          >重試</Button
        >
      </div>
      <div
        class="flex h-8 shrink-0 items-center gap-4 overflow-x-auto whitespace-nowrap border-b px-3 text-xs text-muted-foreground"
      >
        <span>會議 {{ notices?.meetingId || requestedId || '—' }}</span>
        <span>基準日 {{ selectedMeeting?.recordDate || '—' }}</span>
        <span>股東 {{ notices ? `${notices.recipients.length} 人` : '—' }}</span>
      </div>
      <div class="min-h-0 flex-1 overflow-auto">
        <table class="erp-table min-w-[700px]">
          <thead class="sticky top-0 z-10">
            <tr>
              <th class="text-left!">股東代碼</th>
              <th class="text-left!">姓名</th>
              <th class="text-left!">通知地址</th>
              <th>基準日持股（股）</th>
              <th>通知時間</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="recipient in notices?.recipients ?? []" :key="recipient.shareholderId">
              <td class="text-left! font-medium">{{ recipient.shareholderId }}</td>
              <td class="text-left!">{{ recipient.name }}</td>
              <td class="text-left!">{{ recipient.address }}</td>
              <td>{{ recipient.shareQty.toLocaleString('zh-TW') }}</td>
              <td>{{ formatStockTime(recipient.sentTime) }}</td>
            </tr>
            <tr v-if="!notices?.recipients.length">
              <td colspan="5" class="h-24! text-center! text-muted-foreground">
                {{
                  detailLoading
                    ? '載入通知名單中…'
                    : notices
                      ? '此基準日沒有可通知股東'
                      : '選擇會議以預覽通知名單'
                }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
