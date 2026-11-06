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

RAW = Path(os.environ.get('DEMO_RAW_DIR', '/tmp/tablerex-demo-raw'))
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


def make_segment(spec: dict) -> Path:
    """spec: src, start, dur, out name, mode, caption, size (w,h), extra vf."""
    dest = WORK / f"{spec['name']}.mp4"
    w, h = spec['size']
    filters = [f"fps={FPS}", f"setpts=PTS-STARTPTS"]
    if spec.get('vf'):
        filters.append(spec['vf'])
    filters.append(f"scale={w}:{h}:force_original_aspect_ratio=decrease")
    filters.append(f"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=0x0c1a1f")
    filters.append('format=yuv420p')
    if spec.get('caption'):
        filters.append(caption_filter(spec['name'], spec['caption'], spec['dur'], spec.get('csize', 54), spec.get('cy', 'h-150')))
    filters.append(f"fade=t=in:st=0:d=0.18,fade=t=out:st={max(0, spec['dur'] - 0.22):.2f}:d=0.2")
    run(
        [
            'ffmpeg', '-y', '-ss', f"{spec['start']:.3f}", '-t', f"{spec['dur']:.3f}",
            '-i', str(spec['src']),
            '-vf', ','.join(filters),
            '-an', '-r', str(FPS),
            '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '18', '-pix_fmt', 'yuv420p',
            str(dest),
        ]
    )
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
            '-r', str(FPS), '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '18', '-pix_fmt', 'yuv420p',
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


def phone_vf() -> str:
    # Playwright wrote the 430x932 viewport into the top-left of a 2x canvas.
    return 'crop=430:932:0:0,scale=886:1920'


def mask_localhost() -> str:
    # Cover the dev-only public URL bar. The rest of the settings screen stays.
    return 'drawbox=x=248:y=112:w=1080:h=64:color=0xD7E4E7:t=fill'


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
    dest = OUT / f'{name}.mp4'
    mix_audio(silent, music, whoosh, total, whoosh_at, dest)
    print(f'{dest} {total:.2f}s')
    return total


def export_raw() -> None:
    crops = {
        'v_guest': 'crop=430:932:0:0',
        'v_rival': 'crop=430:932:0:0',
    }
    for src in sorted(RAW.glob('*.webm')):
        dest = OUT / f"raw_{src.stem}.mp4"
        vf = crops.get(src.stem)
        cmd = ['ffmpeg', '-y', '-i', str(src)]
        if vf:
            cmd += ['-vf', vf]
        cmd += ['-c:v', 'libx264', '-preset', 'veryfast', '-crf', '18', '-pix_fmt', 'yuv420p', '-an', '-movflags', '+faststart', str(dest)]
        run(cmd)
        print('raw', dest.name)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)
    v = (1080, 1920)
    h = (1920, 1080)

    vertical = [
        dict(name='v_hook', src=RAW / 'v_hook.webm', start=0.1, dur=3.0),
        dict(name='v_book', src=RAW / 'v_guest.webm', start=2.6, dur=5.2, vf=phone_vf(), caption='Guests book online, 24/7', csize=58),
        dict(name='v_confirm', src=RAW / 'v_guest.webm', start=7.6, dur=5.0, vf=phone_vf(), caption='Live availability. Instant confirmation.', csize=48),
        dict(name='v_floor', src=RAW / 'v_floor.webm', start=6.5, dur=5.4, caption='Your team sees it on the live floor view', csize=46),
        dict(name='v_checkin', src=RAW / 'v_floor.webm', start=12.6, dur=4.2, caption='Every section, one view', csize=54),
        dict(name='v_taken', src=RAW / 'v_rival.webm', start=13.2, dur=5.6, vf=phone_vf(), caption='No double bookings. Ever.', csize=58),
        dict(name='v_sections', src=RAW / 'v_sections.webm', start=1.3, dur=4.6, vf=mask_localhost(), caption='Terrace, Main and VIP', csize=58),
        dict(name='v_deposits', src=RAW / 'v_deposits.webm', start=0.15, dur=4.0),
        dict(name='v_end', src=RAW / 'v_end.webm', start=0.15, dur=4.9),
    ]
    horizontal = [
        dict(name='h_title', src=RAW / 'h_title.webm', start=0.15, dur=5.0),
        dict(name='h_guest', src=RAW / 'h_guest.webm', start=4.2, dur=10.2, caption='Guests book online. Confirmed instantly.', csize=52, cy='h-120'),
        dict(name='h_list', src=RAW / 'h_list.webm', start=5.5, dur=7.5, caption="Tonight's bookings, as they land", csize=50, cy='h-110'),
        dict(name='h_floor', src=RAW / 'h_floor.webm', start=7.2, dur=8.5, caption='Live floor. Every section, status colours.', csize=48, cy='h-110'),
        dict(name='h_timeline', src=RAW / 'h_list.webm', start=21.6, dur=3.6, caption='Terrace, Main and VIP on one timeline', csize=48, cy='h-110'),
        dict(name='h_taken', src=RAW / 'h_rival.webm', start=11.5, dur=7.2, caption='That time was just taken. Next slot is 21:00.', csize=46, cy='h-120'),
        dict(name='h_settings', src=RAW / 'h_sections.webm', start=1.2, dur=7.6, vf=mask_localhost(), caption='Slot interval, turnover, hours, special days', csize=44, cy='h-110'),
        dict(name='h_roles', src=RAW / 'h_staff.webm', start=0.8, dur=3.4, caption='Owners, managers, hosts. Each with a role.', csize=46, cy='h-110'),
        dict(name='h_phone', src=RAW / 'h_staff.webm', start=4.6, dur=5.2, caption='A phone booking, added by the team, lands on the same list', csize=42, cy='h-110'),
        dict(name='h_deposits', src=RAW / 'h_deposits.webm', start=0.2, dur=5.0),
        dict(name='h_end', src=RAW / 'h_end.webm', start=0.2, dur=5.8),
    ]
    # Social cuts, 8-15s, same grade.
    cuts = {
        'cut_guest_books': [
            dict(name='c_guest', src=RAW / 'h_guest.webm', start=4.4, dur=6.2, caption='Guests book online, 24/7', csize=52, cy='h-120'),
            dict(name='c_confirm', src=RAW / 'h_guest.webm', start=10.6, dur=3.8, caption='Instant confirmation', csize=56, cy='h-120'),
        ],
        'cut_live_floor': [
            dict(name='c_floor', src=RAW / 'h_floor.webm', start=7.4, dur=8.2, caption='Live floor view', csize=58, cy='h-110'),
            dict(name='c_check', src=RAW / 'h_floor.webm', start=14.8, dur=3.2, caption='Checked in on the floor', csize=52, cy='h-110'),
        ],
        'cut_no_double_booking': [
            dict(name='c_taken', src=RAW / 'h_rival.webm', start=10.4, dur=9.2, caption='No double bookings. Ever.', csize=54, cy='h-120'),
        ],
        'cut_every_section': [
            dict(name='c_time', src=RAW / 'h_list.webm', start=21.5, dur=3.5, caption='Terrace, Main and VIP', csize=56, cy='h-110'),
            dict(name='c_sec', src=RAW / 'h_sections.webm', start=1.4, dur=3.2, vf=mask_localhost(), caption='Every section, one view', csize=54, cy='h-110'),
            dict(name='c_floor2', src=RAW / 'h_floor.webm', start=8.0, dur=4.0, caption='Pushed-together terrace tables', csize=48, cy='h-110'),
        ],
        'cut_phone_booking': [
            dict(name='c_roles', src=RAW / 'h_staff.webm', start=0.7, dur=3.6, caption='Staff roles', csize=58, cy='h-110'),
            dict(name='c_phone', src=RAW / 'h_staff.webm', start=4.4, dur=5.6, caption='Phone bookings land on the same list', csize=46, cy='h-110'),
        ],
    }

    only = sys.argv[1:]
    if not only or 'reel' in only:
        build('demo_reel_vertical', vertical, v)
    if not only or 'horizontal' in only:
        build('demo_horizontal', horizontal, h)
    for cut_name, specs in cuts.items():
        if not only or cut_name in only or 'cuts' in only:
            build(cut_name, specs, h)
    if not only or 'raw' in only:
        export_raw()


if __name__ == '__main__':
    main()
