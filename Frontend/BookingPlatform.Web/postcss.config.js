// https://github.com/michael-ciniawsky/postcss-load-config

import autoprefixer from 'autoprefixer'

export default {
  plugins: [
    autoprefixer({
      overrideBrowserslist: ['baseline widely available'],
    }),
  ],
}
