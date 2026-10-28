import { globalIgnores } from 'eslint/config';
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript';
import skipFormatting from '@vue/eslint-config-prettier/skip-formatting';
import pluginVue from 'eslint-plugin-vue';

export default defineConfigWithVueTs(
  { files: ['**/*.{ts,vue}'] },
  globalIgnores(['dist/**']),
  pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,
  {
    files: ['src/components/ui/**/*.vue'],
    rules: { 'vue/multi-word-component-names': 'off' },
  },
  skipFormatting,
);
