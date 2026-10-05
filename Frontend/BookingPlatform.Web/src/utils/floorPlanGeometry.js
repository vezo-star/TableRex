export const GRID = 10
export const MIN_SIZE = 20

export function snap(value, grid = GRID) {
  return Math.round(value / grid) * grid
}

export function clamp(value, min, max) {
  return Math.min(Math.max(value, min), max)
}

/** Normalises degrees to [0, 360). */
export function normaliseRotation(degrees) {
  const value = Number(degrees) || 0
  return ((value % 360) + 360) % 360
}

/** Moves a shape by (dx, dy), snapped to the grid and kept inside the plan. */
export function moveWithin(position, dx, dy, plan, grid = GRID) {
  const x = clamp(snap(position.x + dx, grid), 0, plan.width - position.width)
  const y = clamp(snap(position.y + dy, grid), 0, plan.height - position.height)
  return { ...position, x, y }
}

/** Resizes from the bottom-right corner, snapped, at least MIN_SIZE and kept inside the plan. */
export function resizeWithin(position, dw, dh, plan, grid = GRID) {
  const width = clamp(snap(position.width + dw, grid), MIN_SIZE, plan.width - position.x)
  const height = clamp(snap(position.height + dh, grid), MIN_SIZE, plan.height - position.y)
  return { ...position, width, height }
}

/** Default footprint for a resource, scaled loosely by capacity. */
export function defaultFootprint(capacity) {
  if (capacity <= 1) {
    return { width: 50, height: 50, shape: 'ROUND' }
  }
  if (capacity <= 2) {
    return { width: 80, height: 80, shape: 'ROUND' }
  }
  if (capacity <= 4) {
    return { width: 120, height: 100, shape: 'RECT' }
  }
  return { width: Math.min(80 + capacity * 15, 320), height: 110, shape: 'RECT' }
}

/** First free grid spot (left-to-right, top-to-bottom) where a shape fits without overlapping others. */
export function findFreeSpot(positions, size, plan, grid = GRID) {
  const margin = grid * 2
  for (let y = margin; y + size.height <= plan.height; y += grid * 4) {
    for (let x = margin; x + size.width <= plan.width; x += grid * 4) {
      const candidate = { x, y, width: size.width, height: size.height }
      if (!positions.some((p) => overlaps(p, candidate, grid))) {
        return { x, y }
      }
    }
  }
  return { x: 0, y: 0 }
}

export function overlaps(a, b, gap = 0) {
  return (
    a.x < b.x + b.width + gap &&
    b.x < a.x + a.width + gap &&
    a.y < b.y + b.height + gap &&
    b.y < a.y + a.height + gap
  )
}

/** True when the (unrotated) bounding box lies inside the plan, matching the API rule. */
export function fitsInside(position, plan) {
  return (
    position.x >= 0 &&
    position.y >= 0 &&
    position.x + position.width <= plan.width &&
    position.y + position.height <= plan.height
  )
}
