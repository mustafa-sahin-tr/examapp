import { defineConfig } from 'vite';

export default defineConfig({
  base: '/app/',
  server: {
    host: true,
    port: 4200,
    allowedHosts: ['angular-app','auth-ui'],
    strictPort: true,
  },
});
