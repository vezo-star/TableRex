#!/usr/bin/env python3
"""Cut the Playwright recordings into the marketing reel, the horizontal demo, and short social clips.

Raw scene files are read from DEMO_RAW_DIR (default /tmp/tablerex-demo-raw).
Finished mp4 files are written to DEMO_OUT_DIR (default /opt/cursor/artifacts).
"""

from __future__ import annotations

import math
import os
import struct
import sys
import subprocess
import wave
from pathlib import Path

RAW = Path(os.environ.get('DEMO_RAW_DIR', '/tmp/tablerex-demo-raw-4k'))
OUT = Path(os.environ.get('DEMO_OUT_DIR', '/opt/cursor/artifacts'))
WORK = Path(os.environ.get('DEMO_WORK_DIR', '/tmp/tablerex-edit'))
FONTS = Path(__file__).resolve().parent / 'fonts'
SERIF = FONTS / 'CormorantGaramond-SemiBold.ttf'
SANS = FONTS / 'Outfit-Medium.ttf'
SR = 48000
FPS = 30
XFADE = 0.28


def run(cmd: list[str]) -> None:
    if cmd and cmd[0] == 'ffmpeg':
        cmd = ['ffmpeg', '-hide_banner', '-loglevel', 'error', *cmd[1:]]
    subprocess.run(cmd, check=True)


def ensure_audio() -> tuple[Path, Path]:
    WORK.mkdir(parents=True, exist_ok=True)
    music = WORK / 'music.wav'
    whoosh = WORK / 'whoosh.wav'
    if not music.exists():
        write_piano(music, seconds=100)
    if not whoosh.exists():
        write_whoosh(whoosh)
    return music, whoosh


def env_decay(t: float, dur: float) -> float:
    if t < 0 or t > dur:
        return 0.0
    attack = 0.012
    if t < attack:
        return t / attack
    return math.exp(-(t - attack) * 2.4)


def write_piano(path: Path, seconds: float) -> None:
    # Soft, sparse piano-like tones in D. Low amplitude; loudnorm sets the final level.
    notes = [146.83, 174.61, 196.00, 220.00, 261.63, 293.66, 329.63, 392.00]
    pattern = [0, 2, 4, 7, 4, 2, 5, 3, 0, 4, 7, 2]
    n = int(SR * seconds)
    left = [0.0] * n
    right = [0.0] * n
    step = int(SR * 1.75)
    for i, degree in enumerate(pattern * 8):
        start = i * step
        if start >= n:
            break
        freq = notes[degree % len(notes)]
        length = int(SR * 2.6)
        for k in range(length):
            idx = start + k
            if idx >= n:
                break
            t = k / SR
            env = env_decay(t, 2.6)
            tone = (
                math.sin(2 * math.pi * freq * t)
                + 0.38 * math.sin(2 * math.pi * freq * 2 * t)
                + 0.12 * math.sin(2 * math.pi * freq * 3 * t)
            )
            # A quiet lower fifth under every other note, so it leads without a beat.
            if i % 2 == 0:
                tone += 0.22 * math.sin(2 * math.pi * (freq / 2) * t)
            sample = tone * env * 0.16
            left[idx] += sample
            right[idx] += sample * (0.92 if i % 2 == 0 else 1.0)
    peak = max(max(abs(s) for s in left), max(abs(s) for s in right), 1e-6)
    scale = 0.7 / peak
    with wave.open(str(path), 'w') as handle:
        handle.setnchannels(2)
        handle.setsampwidth(2)
        handle.setframerate(SR)
        frames = bytearray()
        for a, b in zip(left, right):
            frames += struct.pack('<hh', int(max(-1, min(1, a * scale)) * 32767), int(max(-1, min(1, b * scale)) * 32767))
        handle.writeframes(frames)


def write_whoosh(path: Path) -> None:
    # A short filtered-noise sweep. Mixed very quietly under the transitions.
    dur = 0.42
    n = int(SR * dur)
    rng = random_bytes(n)
    with wave.open(str(path), 'w') as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SR)
        frames = bytearray()
        prev = 0.0
        for i in range(n):
            t = i / SR
            noise = (rng[i] - 128) / 128
            # One-pole lowpass that opens slightly, then a fast fade.
            prev = prev * 0.86 + noise * 0.14
            fade_in = min(1.0, t / 0.04)
            fade_out = min(1.0, (dur - t) / 0.28)
            sample = prev * fade_in * fade_out * 0.22
            frames += struct.pack('<h', int(max(-1, min(1, sample)) * 32767))
        handle.writeframes(frames)


def random_bytes(n: int) -> bytes:
    return os.urandom(n)


def textfile(name: str, text: str) -> Path:
    path = WORK / f'{name}.txt'
    path.write_text(text, encoding='utf-8')
    return path


def caption_filter(name: str, text: str, dur: float, size: int, y: str) -> str:
    path = textfile(name, text)
    alpha = f"if(lt(t,0.32),t/0.32,if(lt(t,{dur - 0.4:.2f}),1,({dur:.2f}-t)/0.4))"
    return (
        f"drawtext=fontfile={SERIF}:textfile={path}:fontsize={size}:fontcolor=0xF7F1E8:"
        f"x=(w-text_w)/2:y={y}:box=1:boxcolor=0x12232ACC:boxborderw=20:alpha='{alpha}'"
    )


# Finished files are published under OUT. That directory rejects anything over 100 MiB,
# so a 64s 1080p film is held at 12.2 Mbps (still above the 12 Mbps floor) and the 4K
# master is encoded at 46 Mbps, then split into parts that each fit.
ARTIFACT_CAP = 99 * 1024 * 1024


def x264(size: tuple[int, int], seconds: float = 20) -> list[str]:
    """H.264 High, yuv420p, 30fps, CBR. CRF 16 of this static UI falls to a few
    hundred kbps, so the rate is set explicitly and filler holds the floor."""
    w, h = size
    gop = ''
    if w >= 3000 or h >= 3000:
        rate, level = '46M', '5.1'
        # Short GOP so the 4K master can be split on keyframes under the 100 MiB cap.
        gop = ':keyint=48:min-keyint=48:scenecut=0'
    elif w * h * seconds > 1920 * 1080 * 48:
        rate, level = '12200k', '4.2'
    else:
        rate, level = '16M', '4.2'
    return [
        '-c:v', 'libx264', '-profile:v', 'high', '-level:v', level,
        '-preset', 'medium',
        '-b:v', rate, '-minrate', rate, '-maxrate', rate, '-bufsize', rate,
        '-x264-params', f'nal-hrd=cbr:filler=1:aq-mode=3{gop}',
        '-pix_fmt', 'yuv420p', '-r', str(FPS),
    ]


def make_segment(spec: dict) -> Path:
    """Downscale a 2× capture with lanczos. Wide shots in the vertical reel sit in a 16:9 panel."""
    dest = WORK / f"{spec['name']}.mp4"
    w, h = spec['size']
    dur = spec['dur']
    cap = ''
    if spec.get('caption'):
        cap = ',' + caption_filter(
            spec['name'], spec['caption'], dur,
            spec.get('csize', 54), spec.get('cy', 'h-150'),
        )
    fade = f",fade=t=in:st=0:d=0.18,fade=t=out:st={max(0, dur - 0.22):.2f}:d=0.2"
    mask_fg = ''
    if spec.get('mask') and spec.get('panel'):
        # Source frames are 3840x2160. Cover the dev-only booking URL.
        mask_fg = 'drawbox=x=470:y=190:w=1750:h=150:color=0xD7E4E7:t=fill,'
    if spec.get('panel'):
        # Full 16:9, edge to edge. zoompan reads the 3840-wide capture straight
        # into the panel so the retina frames are not thrown away first.
        zoom = spec.get('zoom', 0.0)
        cx = spec.get('cx', 0.5)
        cy = spec.get('cy_focus', 0.48)
        frames = max(1, int(dur * FPS))
        hold = int(spec.get('hold', 0))
        if hold and zoom:
            z = f"if(lt(on\\,{hold}),1,1+{zoom:.4f}*(on-{hold})/{max(1, frames - hold)})"
        else:
            z = f"1+{zoom:.4f}*on/{frames}"
        panel_w, panel_h, panel_y = 1080, 608, 420
        graph = (
            f"[0:v]fps={FPS},setpts=PTS-STARTPTS,split[bg][fg];"
            f"[bg]scale=1080:1920:force_original_aspect_ratio=increase:flags=lanczos,crop=1080:1920,"
            f"gblur=sigma=24,eq=brightness=-0.42:saturation=0.85[bg2];"
            f"[fg]{mask_fg}zoompan=z='{z}':x='(iw-iw/zoom)*{cx}':y='(ih-ih/zoom)*{cy}':d=1:s={panel_w}x{panel_h}:fps={FPS}[fg2];"
            f"[bg2][fg2]overlay=(W-w)/2:{panel_y}{cap}{fade},setsar=1,format=yuv420p[vout]"
        )
        run([
            'ffmpeg', '-y', '-ss', f"{spec['start']:.3f}", '-t', f"{dur:.3f}",
            '-i', str(spec['src']),
            '-filter_complex', graph, '-map', '[vout]', '-an', *x264(spec['size']), str(dest),
        ])
        return dest
    mask = ''
    if spec.get('mask'):
        scale = h / 1080
        mask = (
            f",drawbox=x={int(248 * scale)}:y={int(108 * scale)}:w={int(1120 * scale)}:"
            f"h={int(72 * scale)}:color=0xD7E4E7:t=fill"
        )
    vf = (
        f"fps={FPS},setpts=PTS-STARTPTS,"
        f"scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos,"
        f"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=0x0c1a1f{mask},setsar=1,format=yuv420p{cap}{fade}"
    )
    run([
        'ffmpeg', '-y', '-ss', f"{spec['start']:.3f}", '-t', f"{dur:.3f}",
        '-i', str(spec['src']),
        '-vf', vf, '-an', *x264(spec['size']), str(dest),
    ])
    return dest


def concat_xfade(segments: list[Path], durs: list[float], dest: Path, size: tuple[int, int]) -> float:
    if len(segments) == 1:
        run(['ffmpeg', '-y', '-i', str(segments[0]), '-c', 'copy', str(dest)])
        return durs[0]
    inputs: list[str] = []
    for seg in segments:
        inputs += ['-i', str(seg)]
    parts = []
    prev = '[0:v]'
    offset = 0.0
    total = durs[0]
    for i in range(1, len(segments)):
        offset = total - XFADE
        total += durs[i] - XFADE
        out = '[vout]' if i == len(segments) - 1 else f'[vx{i}]'
        parts.append(
            f"{prev}[{i}:v]xfade=transition=fade:duration={XFADE}:offset={offset:.3f}{out}"
        )
        prev = out
    run(
        [
            'ffmpeg', '-y', *inputs,
            '-filter_complex', ';'.join(parts),
            '-map', '[vout]',
            *x264(size, durs[0] + sum(d - XFADE for d in durs[1:])),
            str(dest),
        ]
    )
    return total


def mix_audio(video: Path, music: Path, whoosh: Path, duration: float, whoosh_at: list[float], dest: Path) -> None:
    # Place a quiet whoosh at each transition, then loudnorm the bed to about -23 LUFS.
    delays = []
    mix_inputs = ['[1:a]volume=0.85[music]']
    labels = ['[music]']
    for i, at in enumerate(whoosh_at):
        ms = max(0, int(at * 1000))
        mix_inputs.append(f"[2:a]adelay={ms}|{ms},volume=0.18[w{i}]")
        labels.append(f"[w{i}]")
    # Quiet bed: sparse piano plus soft whooshes, about -23 LUFS after this trim.
    mix = (
        ';'.join(mix_inputs)
        + ';'
        + ''.join(labels)
        + 'amix=inputs='
        + str(len(labels))
        + ':duration=first:dropout_transition=0,alimiter=limit=0.5,volume=-7.4dB[aout]'
    )
    run(
        [
            'ffmpeg', '-y', '-i', str(video), '-i', str(music), '-i', str(whoosh),
            '-filter_complex', mix,
            '-map', '0:v', '-map', '[aout]',
            '-t', f'{duration:.3f}',
            '-c:v', 'copy', '-c:a', 'aac', '-b:a', '160k', '-ar', str(SR), '-ac', '2',
            '-movflags', '+faststart',
            str(dest),
        ]
    )


def build(name: str, specs: list[dict], size: tuple[int, int]) -> float:
    music, whoosh = ensure_audio()
    segs = []
    durs = []
    for spec in specs:
        spec = {**spec, 'size': size}
        segs.append(make_segment(spec))
        durs.append(spec['dur'])
    silent = WORK / f'{name}_silent.mp4'
    total = concat_xfade(segs, durs, silent, size)
    whoosh_at = []
    cursor = 0.0
    for dur in durs[:-1]:
        cursor += dur - XFADE
        whoosh_at.append(cursor)
    staged = WORK / f'{name}.mp4'
    mix_audio(silent, music, whoosh, total, whoosh_at, staged)
    publish(staged, OUT / f'{name}.mp4')
    print(f'{name} {total:.2f}s')
    return total


def integrated_lufs(path: Path) -> float | None:
    proc = subprocess.run(
        ['ffmpeg', '-hide_banner', '-i', str(path), '-af', 'ebur128', '-f', 'null', '-'],
        capture_output=True, text=True,
    )
    measured = None
    for line in proc.stderr.splitlines():
        if 'I:' in line and 'LUFS' in line:
            measured = float(line.split('I:', 1)[1].split('LUFS', 1)[0].strip())
    return measured


def settle_loudness(path: Path) -> None:
    """Short cuts catch a louder slice of the same bed. Nudge them back to about -23 LUFS."""
    probe = subprocess.run(
        ['ffprobe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'default=nw=1:nk=1', str(path)],
        capture_output=True, text=True, check=True,
    )
    if float(probe.stdout.strip()) >= 25:
        return
    measured = integrated_lufs(path)
    if measured is None:
        return
    delta = -23.0 - measured
    if abs(delta) < 1.2:
        return
    tmp = path.with_suffix('.lvl.mp4')
    run([
        'ffmpeg', '-y', '-i', str(path), '-c:v', 'copy',
        '-af', f'volume={delta:.2f}dB', '-c:a', 'aac', '-b:a', '160k',
        '-movflags', '+faststart', str(tmp),
    ])
    tmp.replace(path)


def publish(src: Path, dest: Path) -> None:
    """Copy into the artifact dir. Files over the 100 MiB cap are split on keyframes."""
    if src.stat().st_size <= ARTIFACT_CAP:
        run(['ffmpeg', '-y', '-i', str(src), '-c', 'copy', '-movflags', '+faststart', str(dest)])
        settle_loudness(dest)
        print(f'  {dest.name} {dest.stat().st_size} bytes')
        return
    tmp_pattern = str(WORK / f'{dest.stem}_part%02d{dest.suffix}')
    run([
        'ffmpeg', '-y', '-i', str(src), '-c', 'copy', '-f', 'segment',
        '-segment_time', '14', '-reset_timestamps', '1', tmp_pattern,
    ])
    lines = []
    for part in sorted(WORK.glob(f'{dest.stem}_part*{dest.suffix}')):
        out = dest.with_name(part.name)
        run(['ffmpeg', '-y', '-i', str(part), '-c', 'copy', '-movflags', '+faststart', str(out)])
        if out.stat().st_size > ARTIFACT_CAP:
            raise SystemExit(f'{out.name} is {out.stat().st_size} bytes, over the artifact cap')
        lines.append(f"file '{out.name}'")
        print(f'  {out.name} {out.stat().st_size} bytes')
    dest.with_name(dest.stem + '_concat.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def export_raw() -> None:
    for src in sorted(RAW.glob('*.mp4')):
        dest = OUT / f"raw_{src.stem}.mp4"
        run(['ffmpeg', '-y', '-i', str(src), '-c', 'copy', '-movflags', '+faststart', str(dest)])
        print('raw', dest.name)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)
    v = (1080, 1920)
    h = (1920, 1080)
    uhd = (3840, 2160)

    # Wide UI stays a full 16:9 panel on the 9:16 frame (never sliced to fill the height).
    # The floor holds the whole screen, then eases into the tables. End zoom still
    # contains TR1–TR3, M2, M4, M6, V1 and V2.
    vertical = [
        dict(name='v_hook', src=RAW / 'v_hook.mp4', start=0.15, dur=3.2),
        dict(name='v_book', src=RAW / 'h_guest.mp4', start=1.4, dur=4.6, panel=True, zoom=0.06, caption='Guests book online, 24/7', csize=52),
        dict(name='v_confirm', src=RAW / 'h_guest.mp4', start=6.4, dur=3.4, panel=True, zoom=0.05, caption='Live availability. Instant confirmation.', csize=44),
        dict(name='v_floor', src=RAW / 'h_floor.mp4', start=0.4, dur=6.2, panel=True, zoom=0.40, hold=36, cx=0.46, cy_focus=0.50, caption='Your team sees it on the live floor view', csize=42),
        dict(name='v_checkin', src=RAW / 'h_floor.mp4', start=9.0, dur=4.6, panel=True, caption='Checked in. Every section, one view', csize=42),
        dict(name='v_taken', src=RAW / 'h_rival.mp4', start=6.0, dur=7.4, panel=True, zoom=0.06, caption='No double bookings. Ever.', csize=52),
        dict(name='v_sections', src=RAW / 'h_sections.mp4', start=0.25, dur=4.4, panel=True, mask=True, zoom=0.04, caption='Terrace, Main and VIP', csize=52),
        dict(name='v_deposits', src=RAW / 'v_deposits.mp4', start=0.15, dur=4.2),
        dict(name='v_end', src=RAW / 'v_end.mp4', start=0.15, dur=5.2),
    ]
    horizontal = [
        dict(name='h_title', src=RAW / 'h_title.mp4', start=0.2, dur=5.1),
        dict(name='h_guest', src=RAW / 'h_guest.mp4', start=1.2, dur=8.6, caption='Guests book online. Confirmed instantly.', csize=52, cy='h-120'),
        dict(name='h_list', src=RAW / 'h_list.mp4', start=0.3, dur=2.3, caption="Tonight's bookings, as they land", csize=50, cy='h-110'),
        dict(name='h_timeline', src=RAW / 'h_list.mp4', start=2.8, dur=2.8, caption='Terrace, Main and VIP on one timeline', csize=48, cy='h-110'),
        dict(name='h_floor', src=RAW / 'h_floor.mp4', start=0.4, dur=8.2, caption='Live floor. Every section, status colours.', csize=48, cy='h-110'),
        dict(name='h_check', src=RAW / 'h_floor.mp4', start=8.8, dur=4.9, caption='Checked in on the floor', csize=52, cy='h-110'),
        dict(name='h_taken', src=RAW / 'h_rival.mp4', start=5.0, dur=8.8, caption='That time was just taken. Next slot is 21:00.', csize=46, cy='h-120'),
        dict(name='h_settings', src=RAW / 'h_sections.mp4', start=0.2, dur=6.8, mask=True, caption='Slot interval, turnover, hours, special days', csize=44, cy='h-110'),
        dict(name='h_roles', src=RAW / 'h_staff.mp4', start=0.2, dur=3.0, caption='Owners, managers, hosts. Each with a role.', csize=46, cy='h-110'),
        dict(name='h_phone', src=RAW / 'h_staff.mp4', start=4.2, dur=5.8, caption='A phone booking, added by the team, lands on the same list', csize=42, cy='h-110'),
        dict(name='h_deposits', src=RAW / 'h_deposits.mp4', start=0.2, dur=5.1),
        dict(name='h_end', src=RAW / 'h_end.mp4', start=0.2, dur=6.0),
    ]
    cuts = {
        'cut_guest_books': [
            dict(name='c_guest', src=RAW / 'h_guest.mp4', start=1.4, dur=5.0, caption='Guests book online, 24/7', csize=52, cy='h-120'),
            dict(name='c_confirm', src=RAW / 'h_guest.mp4', start=6.4, dur=3.4, caption='Instant confirmation', csize=56, cy='h-120'),
        ],
        'cut_live_floor': [
            dict(name='c_floor', src=RAW / 'h_floor.mp4', start=0.4, dur=7.4, caption='Live floor view', csize=58, cy='h-110'),
            dict(name='c_check', src=RAW / 'h_floor.mp4', start=9.2, dur=4.4, caption='Checked in on the floor', csize=52, cy='h-110'),
        ],
        'cut_no_double_booking': [
            dict(name='c_taken', src=RAW / 'h_rival.mp4', start=4.5, dur=9.2, caption='No double bookings. Ever.', csize=54, cy='h-120'),
        ],
        'cut_every_section': [
            dict(name='c_time', src=RAW / 'h_list.mp4', start=2.8, dur=2.8, caption='Terrace, Main and VIP', csize=56, cy='h-110'),
            dict(name='c_sec', src=RAW / 'h_sections.mp4', start=0.25, dur=3.2, mask=True, caption='Every section, one view', csize=54, cy='h-110'),
            dict(name='c_floor2', src=RAW / 'h_floor.mp4', start=0.6, dur=4.2, caption='Pushed-together terrace tables', csize=48, cy='h-110'),
        ],
        'cut_phone_booking': [
            dict(name='c_roles', src=RAW / 'h_staff.mp4', start=0.2, dur=3.2, caption='Staff roles', csize=58, cy='h-110'),
            dict(name='c_phone', src=RAW / 'h_staff.mp4', start=4.4, dur=5.6, caption='Phone bookings land on the same list', csize=46, cy='h-110'),
        ],
    }

    only = sys.argv[1:]
    if not only or 'reel' in only:
        build('demo_reel_vertical', vertical, v)
    if not only or 'horizontal' in only:
        build('demo_horizontal', horizontal, h)
    if not only or '4k' in only:
        fourk = []
        for spec in horizontal:
            item = {**spec, 'name': 'k_' + spec['name']}
            if 'csize' in item:
                item['csize'] = int(item['csize'] * 2)
            cy = item.get('cy')
            if isinstance(cy, str) and cy.startswith('h-'):
                item['cy'] = f"h-{int(cy[2:]) * 2}"
            fourk.append(item)
        build('demo_horizontal_4k', fourk, uhd)
    for cut_name, specs in cuts.items():
        if not only or cut_name in only or 'cuts' in only:
            build(cut_name, specs, h)
    if not only or 'raw' in only:
        export_raw()


if __name__ == '__main__':
    main()
