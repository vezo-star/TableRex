// https://v2.quasar.dev/quasar-cli-vite/quasar-config-file

import { defineConfig } from '#q-app'

const API_DEV_TARGET = 'http://localhost:5080'

export default defineConfig(() => {
  return {
    boot: ['axios', 'auth'],

    css: ['app.css'],

    extras: ['material-icons'],

    build: {
      target: {
        browser: 'baseline-widely-available',
      },

      vueRouterMode: 'history',

      // Exposes VITE_* variables (process env or Frontend/BookingPlatform.Web/.env)
      // to client code as import.meta.env.VITE_*. Never put secrets in VITE_* values.
      env: {
        clientPrefix: 'VITE_',
      },
    },

    devServer: {
      port: 9000,
      open: false,
      // Same-origin proxy so the refresh cookie (D-011) works in development.
      proxy: {
        '/api': { target: API_DEV_TARGET },
        '/health': { target: API_DEV_TARGET },
        '/hubs': { target: API_DEV_TARGET, ws: true },
      },
    },

    framework: {
      config: {},
      plugins: ['Notify', 'Dialog'],
    },

    animations: [],
  }
})
