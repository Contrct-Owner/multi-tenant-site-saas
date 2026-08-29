import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// dev proxy: the console and API share an origin so the HttpOnly session
// cookie (ADR 21) just works. Point PREMISE_API at the running API.
const apiTarget = process.env.PREMISE_API ?? 'http://localhost:5293';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: Number(process.env.PORT ?? 5173), // Aspire assigns via PORT
    strictPort: true,
    proxy: Object.fromEntries(
      ['/api', '/auth', '/me', '/objects', '/openapi', '/contact-links', '/contact'].map((p) => [
        p,
        { target: apiTarget, changeOrigin: false },
      ]),
    ),
  },
});
