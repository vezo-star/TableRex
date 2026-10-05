const COLOR = /^#[0-9a-f]{6}$/i

/** The colour when it is a plain #rrggbb value, otherwise null (never interpolate untrusted CSS). */
export function safeBrandColor(color) {
  return typeof color === 'string' && COLOR.test(color) ? color.toLowerCase() : null
}

/** The URL when it is an absolute https URL without credentials, otherwise null. */
export function safeLogoUrl(url) {
  if (typeof url !== 'string' || url.length > 500) {
    return null
  }
  try {
    const parsed = new URL(url)
    return parsed.protocol === 'https:' && !parsed.username && !parsed.password ? parsed.href : null
  } catch {
    return null
  }
}

function channel(value) {
  const c = value / 255
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}

/** WCAG relative luminance of a #rrggbb colour. */
export function luminance(color) {
  const hex = color.slice(1)
  const [r, g, b] = [0, 2, 4].map((i) => Number.parseInt(hex.slice(i, i + 2), 16))
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

/** WCAG contrast ratio of white text on the colour. */
export function contrastWithWhite(color) {
  return 1.05 / (luminance(color) + 0.05)
}

/**
 * Inline style that re-themes Quasar's primary colour inside a public page. Only applied when white text on the
 * colour keeps at least 3:1 contrast (WCAG AA for buttons); otherwise the default theme stays readable.
 */
export function brandStyle(branding) {
  const color = safeBrandColor(branding?.primaryColor)
  if (!color || contrastWithWhite(color) < 3) {
    return {}
  }
  return { '--q-primary': color }
}

/** Accent colour for decorative elements (the header band); any valid colour is fine here. */
export function brandAccent(branding) {
  return safeBrandColor(branding?.primaryColor)
}
