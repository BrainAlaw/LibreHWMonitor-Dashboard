import { defineConfig } from 'vitest/config';

export default defineConfig({
  base: './',
  build: {
    outDir: '../src/PCMonitor/wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false
  },
  server: {
    proxy: { '/api': 'http://127.0.0.1:8096' }
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts']
  }
});
