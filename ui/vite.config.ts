import { defineConfig } from 'vitest/config';

export default defineConfig({
  base: './',
  build: {
    outDir: '../src/PCMonitor/wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    // Stable file names: the exe embeds wwwroot, and the app serves it with no-store anyway.
    rollupOptions: {
      output: {
        entryFileNames: 'assets/app.js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name][extname]'
      }
    }
  },
  server: {
    proxy: { '/api': 'http://127.0.0.1:8096' }
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts']
  }
});
