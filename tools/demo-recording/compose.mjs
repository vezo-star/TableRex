/**
 * Cuts the Playwright clips into a vertical Reel and a horizontal demo.
 *   node tools/demo-recording/compose.mjs
 * Reads /tmp/tablerex-demo/raw and writes mp4s under /opt/cursor/artifacts.
 */
import { spawnSync } from 'node:child_process'
import { mkdirSync, writeFileSync, copyFileSync, rmSync } from 'node:fs'

const RAW = '/tmp/tablerex-demo/raw'
const WORK = '/tmp/tablerex-demo/edit'
const ART = '/opt/cursor/artifacts'
const DISPLAY = '/tmp/tablerex-demo/Fraunces-640.ttf'
const SANS = '/tmp/tablerex-demo/Outfit-560.ttf'

const CLIPS = {
  guest: `${RAW}/01-guest-mobile.webm`,
  staff: `${RAW}/02-staff-list.webm`,
  floor: `${RAW}/03-floor-checkin.webm`,
  double: `${RAW}/04-double-book.webm`,
  phone: `${RAW}/05-phone-booking.webm`,
  settings: `${RAW}/06-venue-settings.webm`,
  accounts: `${RAW}/07-staff-accounts.webm`,
}

mkdirSync(WORK, { recursive: true })
mkdirSync(ART, { recursive: true })

let textSeq = 0
function textFile(lines) {
  const path = `${WORK}/t${textSeq++}.txt`
  writeFileSync(path, lines.join('\n'))
  return path
}

function ffmpeg(args) {
  const result = spawnSync('ffmpeg', ['-y', '-hide_banner', '-loglevel', 'error', ...args], {
    encoding: 'utf8',
  })
  if (result.status !== 0) {
    console.error(result.stderr)
    throw new Error('ffmpeg failed')
  }
}

const enc = [
  '-c:v', 'libx264',
  '-preset', 'veryfast',
  '-crf', '18',
  '-pix_fmt', 'yuv420p',
  '-r', '30',
  '-c:a', 'aac',
  '-b:a', '48k',
  '-ac', '2',
  '-ar', '44100',
]

function card(out, { w, h, lines, duration, size = 68 }) {
  const file = textFile(lines)
  ffmpeg([
    '-f', 'lavfi', '-i', `color=c=0x12232a:size=${w}x${h}:rate=30:duration=${duration}`,
    '-f', 'lavfi', '-t', String(duration), '-i', 'anullsrc=channel_layout=stereo:sample_rate=44100',
    '-filter_complex',
    `[0:v]drawtext=fontfile=${DISPLAY}:textfile=${file}:fontcolor=0xf7f1e8:fontsize=${size}:line_spacing=18:` +
      `x=(w-text_w)/2:y=(h-text_h)/2[v]`,
    '-map', '[v]',
    '-map', '1:a',
    ...enc,
    out,
  ])
}

function phone(out, { w, h, start, end, lines, size = 52, phoneH }) {
  const duration = +(end - start).toFixed(3)
  const file = textFile(lines)
  const phoneW = Math.round((phoneH * 390) / 844)
  const y = lines.length ? Math.round(h * 0.22) : Math.round((h - phoneH) / 2)
  const caption = lines.length
    ? `;[stage]drawtext=fontfile=${DISPLAY}:textfile=${file}:fontcolor=0xf7f1e8:fontsize=${size}:line_spacing=12:x=(w-text_w)/2:y=${Math.round(h * 0.07)}[v]`
    : ''
  ffmpeg([
    '-i', CLIPS.guest,
    '-f', 'lavfi', '-t', String(duration), '-i', `color=c=0x12232a:size=${w}x${h}:rate=30`,
    '-f', 'lavfi', '-t', String(duration), '-i', 'anullsrc=channel_layout=stereo:sample_rate=44100',
    '-filter_complex',
    `[0:v]trim=start=${start}:end=${end},setpts=PTS-STARTPTS,fps=30,scale=${phoneW}:${phoneH}:flags=lanczos[phone];` +
      `[1:v]fps=30[bg];` +
      `[bg][phone]overlay=(W-w)/2:${y}${caption ? '[stage]' : '[v]'}` +
      caption,
    '-map', '[v]',
    '-map', '2:a',
    ...enc,
    out,
  ])
}

function desk(out, { w, h, input, start, end, lines, size = 42, delogo = false, mode }) {
  const duration = +(end - start).toFixed(3)
  const file = textFile(lines)
  const logo = delogo ? ',delogo=x=740:y=980:w=440:h=90' : ''
  let graph
  if (mode === 'vertical') {
    graph =
      `[0:v]trim=start=${start}:end=${end},setpts=PTS-STARTPTS,fps=30${logo},scale=1040:-2:flags=lanczos[clip];` +
      `[1:v]fps=30[bg];` +
      `[bg][clip]overlay=(W-w)/2:760[stage];` +
      `[stage]drawtext=fontfile=${DISPLAY}:textfile=${file}:fontcolor=0xf7f1e8:fontsize=${size}:line_spacing=12:` +
      `x=(w-text_w)/2:y=280[v]`
  } else {
    const bar = lines.length
      ? `,drawbox=x=0:y=ih-92:w=iw:h=92:color=0x12232a@0.94:t=fill,` +
        `drawtext=fontfile=${SANS}:textfile=${file}:fontcolor=0xf7f1e8:fontsize=${size}:` +
        `x=(w-text_w)/2:y=h-62`
      : ''
    graph = `[0:v]trim=start=${start}:end=${end},setpts=PTS-STARTPTS,fps=30${logo},scale=1920:1080:flags=lanczos${bar}[v]`
  }
  const inputs = ['-i', input]
  const maps = ['-map', '[v]']
  if (mode === 'vertical') {
    inputs.push('-f', 'lavfi', '-t', String(duration), '-i', `color=c=0x12232a:size=${w}x${h}:rate=30`)
  }
  inputs.push('-f', 'lavfi', '-t', String(duration), '-i', 'anullsrc=channel_layout=stereo:sample_rate=44100')
  const audioIndex = mode === 'vertical' ? '2:a' : '1:a'
  ffmpeg([...inputs, '-filter_complex', graph, ...maps, '-map', audioIndex, ...enc, out])
}

function concat(segments, out) {
  const list = `${WORK}/concat.txt`
  writeFileSync(list, segments.map((file) => `file '${file}'`).join('\n'))
  ffmpeg([
    '-f', 'concat', '-safe', '0', '-i', list,
    '-c:v', 'libx264', '-preset', 'medium', '-crf', '26', '-pix_fmt', 'yuv420p',
    '-r', '30', '-c:a', 'aac', '-b:a', '64k', '-movflags', '+faststart',
    out,
  ])
}

const parts = []
function seg(name) {
  const path = `${WORK}/${name}.mp4`
  parts.push(path)
  return path
}

// --- Vertical reel, 1080x1920 ---
const V = { w: 1080, h: 1920 }
card(seg('v00'), {
  ...V,
  duration: 2.6,
  size: 64,
  lines: ['Still taking bookings', 'in WhatsApp?'],
})
phone(seg('v01'), {
  ...V,
  start: 0.4,
  end: 5.4,
  phoneH: 1280,
  size: 48,
  lines: ['Guests book online, 24/7'],
})
phone(seg('v02'), {
  ...V,
  start: 9.2,
  end: 14.6,
  phoneH: 1360,
  size: 44,
  lines: [],
})
phone(seg('v03'), {
  ...V,
  start: 15.4,
  end: 18.4,
  phoneH: 1180,
  size: 46,
  lines: ['Live availability.', 'Instant confirmation.'],
})
desk(seg('v04'), {
  ...V,
  mode: 'vertical',
  input: CLIPS.staff,
  start: 19.2,
  end: 23.4,
  size: 46,
  lines: ['Your team sees the booking', 'on the live list'],
})
desk(seg('v05'), {
  ...V,
  mode: 'vertical',
  input: CLIPS.floor,
  start: 1.2,
  end: 7.2,
  delogo: true,
  size: 44,
  lines: ['Your team sees it', 'on the live floor view'],
})
desk(seg('v06'), {
  ...V,
  mode: 'vertical',
  input: CLIPS.double,
  start: 2.2,
  end: 6.6,
  size: 48,
  lines: ['No double bookings.', 'Ever.'],
})
desk(seg('v07'), {
  ...V,
  mode: 'vertical',
  input: CLIPS.floor,
  start: 9.5,
  end: 12.6,
  delogo: true,
  size: 48,
  lines: ['Every section, one view'],
})
card(seg('v08'), {
  ...V,
  duration: 2.4,
  size: 64,
  lines: ['Deposits: coming soon'],
})
card(seg('v09'), {
  ...V,
  duration: 4.2,
  size: 52,
  lines: ['Built in Durban', 'for KZN venues.', 'Book a 15-min demo:', 'tablerex.co.za'],
})

const verticalParts = [...parts]
parts.length = 0

// --- Horizontal demo, 1920x1080 ---
const H = { w: 1920, h: 1080, mode: 'horizontal' }
card(seg('h00'), {
  ...H,
  duration: 2.8,
  size: 72,
  lines: ['Still taking bookings in WhatsApp?'],
})
phone(seg('h01'), {
  ...H,
  start: 0.3,
  end: 8.6,
  phoneH: 820,
  size: 42,
  lines: ['Guests book online, 24/7'],
})
phone(seg('h02'), {
  ...H,
  start: 9.0,
  end: 18.4,
  phoneH: 780,
  size: 40,
  lines: ['Live availability. Instant confirmation.'],
})
desk(seg('h03'), {
  ...H,
  input: CLIPS.staff,
  start: 18.8,
  end: 24.4,
  size: 36,
  lines: ["The new booking appears on tonight's list"],
})
desk(seg('h04'), {
  ...H,
  input: CLIPS.floor,
  start: 0.8,
  end: 8.4,
  delogo: true,
  size: 36,
  lines: ['Live floor view. Check a guest in.'],
})
desk(seg('h05'), {
  ...H,
  input: CLIPS.floor,
  start: 9.4,
  end: 13.6,
  delogo: true,
  size: 36,
  lines: ['Every section, one view'],
})
desk(seg('h06'), {
  ...H,
  input: CLIPS.double,
  start: 1.6,
  end: 7.4,
  size: 36,
  lines: ['No double bookings. Ever. The next open slot is offered.'],
})
desk(seg('h07'), {
  ...H,
  input: CLIPS.phone,
  start: 0.6,
  end: 15.2,
  delogo: true,
  size: 34,
  lines: ['A phone booking. Noted as WhatsApp. Combined VIP tables.'],
})
desk(seg('h08'), {
  ...H,
  input: CLIPS.settings,
  start: 0.4,
  end: 6.6,
  size: 34,
  lines: ['15-minute slots. Open 17:00-22:30, Africa/Johannesburg.'],
})
desk(seg('h09'), {
  ...H,
  input: CLIPS.accounts,
  start: 0.3,
  end: 2.3,
  size: 34,
  lines: ['Staff accounts for the demo venue'],
})
card(seg('h10'), {
  ...H,
  duration: 2.2,
  size: 64,
  lines: ['Deposits: coming soon'],
})
card(seg('h11'), {
  ...H,
  duration: 5.2,
  size: 56,
  lines: ['Free for 3 months, no contract.', 'Book a 15-min demo: tablerex.co.za'],
})

const horizontalParts = [...parts]

console.log('Concatenating reel')
concat(verticalParts, `${ART}/tablerex_reel_vertical.mp4`)
console.log('Concatenating demo')
concat(horizontalParts, `${ART}/tablerex_demo_horizontal.mp4`)

const rawNames = {
  '01-guest-mobile.webm': 'raw_guest_mobile_booking.webm',
  '02-staff-list.webm': 'raw_staff_list_live_update.webm',
  '03-floor-checkin.webm': 'raw_floor_checkin_and_sections.webm',
  '04-double-book.webm': 'raw_double_booking_blocked.webm',
  '05-phone-booking.webm': 'raw_phone_whatsapp_booking.webm',
  '06-venue-settings.webm': 'raw_venue_hours_and_sections.webm',
  '07-staff-accounts.webm': 'raw_staff_accounts.webm',
}
for (const [src, dest] of Object.entries(rawNames)) {
  copyFileSync(`${RAW}/${src}`, `${ART}/${dest}`)
}

rmSync(`${WORK}/concat.txt`)
console.log('Wrote', ART)
