// jsdom lacks browser APIs that Quasar uses on install (ScreenOrientation)
// and in QLayout/QResizeObserver (ResizeObserver). Layout is not measured in jsdom.
if (!window.screen.orientation) {
  Object.defineProperty(window.screen, 'orientation', {
    configurable: true,
    value: {
      type: 'landscape-primary',
      angle: 0,
      addEventListener() {},
      removeEventListener() {},
    },
  })
}

if (!globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
}
