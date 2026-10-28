<script setup lang="ts">
import { RouterView } from 'vue-router';
import { Toaster } from 'vue-sonner';
import AppLayout from './layouts/AppLayout.vue';
</script>

<template>
  <!-- 全站骨架與目前功能頁分開：路由切換內容，共用選單及 Header 持續保留。 -->
  <AppLayout>
    <RouterView />
  </AppLayout>
  <!-- 成功提示集中於應用程式根層，功能頁透過 toast 呼叫，不各自建立提示容器。 -->
  <Toaster position="bottom-right" rich-colors close-button class="kgi-toaster" />
</template>
