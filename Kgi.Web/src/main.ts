import { createApp } from 'vue';
// 先載入提示元件的基礎樣式，再由專案主題統一色彩與尺寸。
import 'vue-sonner/style.css';
import './styles/main.css';
import App from './App.vue';
import { router } from './router';

// 應用程式啟動入口：掛上全站路由，由 App 組合外框與功能頁。
createApp(App).use(router).mount('#app');
