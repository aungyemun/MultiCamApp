"""Synthetic test videos with known visual-stability answers (stdlib only; ffmpeg encodes losslessly).

Each case is written as <out>/<case>/cam1/cam1.mp4 (+ cam1_timestamps.csv where noted) and the known
values go to <out>/truth.json. Frames are 320x180 yuv420p (limited) or yuvj420p (full range); Y is set directly (U = V = 128), so the
"% of luma" in the truth values means % of the luma above black level (Y' - 16), as in the app.
Usage: python synth_visual.py <ffmpeg.exe> <out_dir>
"""
import json, math, os, random, subprocess, sys

FFMPEG, OUT = sys.argv[1], sys.argv[2]
W, H, FPS = 320, 180, 30
random.seed(7)


def base_row(y):              # static vertical gradient (the "scene"), must be removed by the audit
    return 90 + 70 * y / (H - 1)


def frame_y(i, t, band=0.0, k=4, drift=0.0, gain=1.0, noise=0.0, black=16):
    rows = []
    for y in range(H):
        # Modulation is applied to the luma above black level (Y' - 16), the audit's definition of "% of luma".
        v = black + (base_row(y) - black) * gain * (1 + band * math.cos(2 * math.pi * (k * y / H + drift * t)))
        if noise:
            rows.append(bytes(max(0, min(255, int(round(v + random.gauss(0, noise))))) for _ in range(W)))
        else:
            rows.append(bytes([max(0, min(255, int(round(v))))]) * W)
    return b"".join(rows) + bytes([128]) * (W * H // 2)


def encode(path, frames, keep=None, full=False):
    """frames: list of raw yuv420p frames at 30 fps; keep: optional predicate on frame number -> VFR file."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    vf = [] if keep is None else ["-vf", f"select='{keep}'", "-fps_mode", "passthrough"]
    pf = ["-pix_fmt", "yuvj420p", "-color_range", "pc"] if full else ["-pix_fmt", "yuv420p"]
    p = subprocess.run([FFMPEG, "-y", "-v", "error", "-f", "rawvideo", *pf, "-s", f"{W}x{H}",
                        "-r", str(FPS), "-i", "-", *vf, "-c:v", "libx264", "-qp", "0", "-preset", "ultrafast",
                        *pf, path], input=b"".join(frames), capture_output=True)
    if p.returncode: raise SystemExit(p.stderr.decode())


def write_csv(path, times_s):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write("frameIndex,captureTimestampUtc,capturePtsMsFromFirstFrame\r\n")
        for i, t in enumerate(times_s):
            f.write(f"{i},2026-11-06T00:00:00Z,{(t - times_s[0]) * 1000:.3f}\r\n")


truth = {}
N = 300                                          # 10 s at 30 fps
T = [i / FPS for i in range(N)]

def case(name, frames, expect, keep=None, csv_times=None, full=False):
    mp4 = os.path.join(OUT, name, "cam1", "cam1.mp4")
    encode(mp4, frames, keep, full)
    if csv_times is not None: write_csv(os.path.join(OUT, name, "cam1", "cam1_timestamps.csv"), csv_times)
    truth[name] = expect
    print("wrote", name)

case("clean", [frame_y(i, t) for i, t in enumerate(T)],
     {"bandPct": 0.0, "flashes": 0, "ydif": 0.0})
case("bands_1pct_k4_up05", [frame_y(i, t, band=0.01, k=4, drift=0.5) for i, t in enumerate(T)],
     {"bandPct": 1.0, "k": 4, "driftHz": 0.5, "dir": "up", "flashes": 0})
case("bands_3pct_k2_down12", [frame_y(i, t, band=0.03, k=2, drift=-1.2) for i, t in enumerate(T)],
     {"bandPct": 3.0, "k": 2, "driftHz": 1.2, "dir": "down", "flashes": 0})
case("bands_05pct_noise1", [frame_y(i, t, band=0.005, k=3, drift=0.7, noise=1.0) for i, t in enumerate(T)],
     {"bandPct": 0.5, "k": 3, "driftHz": 0.7, "dir": "up", "ydif": 2 / math.sqrt(math.pi) * 1.0})
case("flash_3frames", [frame_y(i, t, gain=1.10 if i in (100, 101, 200) else 1.0) for i, t in enumerate(T)],
     {"bandPct": 0.0, "flashes": 3, "lumaRangePct": 10.0})
case("noise_sigma2", [frame_y(i, t, noise=2.0) for i, t in enumerate(T)],
     {"bandPct": 0.0, "flashes": 0, "ydif": 2 / math.sqrt(math.pi) * 2.0})

# Full range (like this app's own recordings, yuvj420p / pc): black level is 0.
case("full_bands_2pct_k3_down08", [frame_y(i, t, band=0.02, k=3, drift=-0.8, black=0) for i, t in enumerate(T)],
     {"bandPct": 2.0, "k": 3, "driftHz": 0.8, "dir": "down", "flashes": 0}, full=True)
case("full_flash_2frames", [frame_y(i, t, gain=1.08 if i in (60, 240) else 1.0, black=0) for i, t in enumerate(T)],
     {"bandPct": 0.0, "flashes": 2, "lumaRangePct": 8.0}, full=True)

# Variable frame rate: every 3rd frame removed (gaps of 2 frame periods), real times kept in the MP4.
N2 = 450
T2 = [i / FPS for i in range(N2)]
kept = [t for i, t in enumerate(T2) if i % 3 != 2]
vfr_frames = [frame_y(i, t, band=0.02, k=4, drift=1.0) for i, t in enumerate(T2)]
case("vfr_bands_csv", vfr_frames, {"bandPct": 2.0, "k": 4, "driftHz": 1.0, "dir": "up", "source": "csv", "frames": len(kept)},
     keep="not(eq(mod(n\\,3)\\,2))", csv_times=kept)
# Same video, but the CSV belongs to another recording (10 extra rows): must fall back to packet times.
case("vfr_bands_wrongcsv", vfr_frames, {"bandPct": 2.0, "k": 4, "driftHz": 1.0, "dir": "up", "source": "packets(rows)"},
     keep="not(eq(mod(n\\,3)\\,2))", csv_times=kept + [kept[-1] + (j + 1) / FPS for j in range(10)])
# Same video without a CSV.
case("vfr_bands_nocsv", vfr_frames, {"bandPct": 2.0, "k": 4, "driftHz": 1.0, "dir": "up", "source": "packets(no csv)"},
     keep="not(eq(mod(n\\,3)\\,2))")

json.dump(truth, open(os.path.join(OUT, "truth.json"), "w"), indent=1)
