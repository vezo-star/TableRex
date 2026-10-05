/** Lowercase, hyphen-separated slug matching the API rule `^[a-z0-9]+(-[a-z0-9]+)*$` (max 63). */
export function slugify(value) {
  return (value ?? '')
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 63)
    .replace(/-+$/g, '')
}
