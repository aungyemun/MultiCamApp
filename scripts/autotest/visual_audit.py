"""Visual stability audit for one video (test harness; stdlib only, ffmpeg does the heavy lifting).

Measures, on the first --seconds of the file:
  * rolling bands  - horizontal bright/dark bands that drift up/down (mains-light flicker vs rolling shutter).
                     Each frame -> 180-row luma profile (16-bit); static scene removed; spatial DFT over rows
                     (k = 1..8 bands per frame height) x temporal DFT using the real frame PTS. A drifting band
                     is a single sharp (k, f) peak; scene motion is broadband. Reports peak amplitude (% of luma,
                     sinusoid peak), temporal frequency, direction and peak-to-median significance.
  * flash / brightness flicker - global mean luma: sudden frame-to-frame jumps (> 4 % vs local median)
                     and the strongest periodic component (> 0.5 Hz).
  * temporal noise - median of signalstats YDIF (mean |Y(t)-Y(t-1)|, 8-bit levels); motion is sparse in
                     time, so the median approximates sensor + coding noise on static scenes.
  * blockiness     - ffmpeg blockdetect (8x8 grid) median; compression artefact indicator.
Usage: python visual_audit.py <video> [--seconds 60]   -> prints JSON
"""
import cmath, json, math, os, statistics, struct, subprocess, sys

FF = os.environ.get("MCA_FFMPEG_DIR") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "runtime", "ffmpeg")
FFMPEG, FFPROBE = os.path.join(FF, "ffmpeg.exe"), os.path.join(FF, "ffprobe.exe")
ROWS, KMAX = 180, 8
KYMO = None


def _run(args, timeout, binary=False):
    try:
        p = subprocess.run(args, capture_output=True, timeout=timeout, stdin=subprocess.DEVNULL)
        return p.returncode, p.stdout if binary else p.stdout.decode("utf-8", "replace"), p.stderr.decode("utf-8", "replace")
    except subprocess.TimeoutExpired:
        return -9, b"" if binary else "", f"TIMEOUT {timeout}s"


def _pts(path, n):
    rc, out, _ = _run([FFPROBE, "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time",
                       "-of", "csv=p=0", path], 300)
    t = sorted(float(x) for x in out.split() if x not in ("", "N/A"))[:n]
    return [x - t[0] for x in t] if t else []


def rolling_bands(prof, t, g):
    n = len(prof)
    m = [sum(prof[i][y] for i in range(n)) / n for y in range(ROWS)]
    gm = sum(m) / ROWS
    res = [[prof[i][y] / g[i] - m[y] / gm for y in range(ROWS)] for i in range(n)]
    tw = [[cmath.exp(-2j * math.pi * k * y / ROWS) for y in range(ROWS)] for k in range(1, KMAX + 1)]
    ck = [[sum(r[y] * w[y] for y in range(ROWS)) for r in res] for w in tw]            # [k][t]
    span = t[-1] - t[0] if n > 1 else 1
    fps = (n - 1) / span if span > 0 else 30
    fmax, df = fps / 2, max(0.1, 1 / span)
    # Slow drift matters: a near-multiple of the light flicker rate (e.g. 120 Hz vs 4 x 29.97 fps) gives
    # bands that crawl over seconds. Search down to max(0.1 Hz, 2 cycles per window).
    fmin = max(0.1, 2 / span)
    freqs = [f * df for f in range(-int(fmax / df), int(fmax / df) + 1) if abs(f * df) >= fmin]
    best, mags = None, []
    for f in freqs:
        rot = [cmath.exp(-2j * math.pi * f * ti) for ti in t]
        for k in range(KMAX):
            s = abs(sum(a * b for a, b in zip(ck[k], rot)))
            mags.append(s)
            if best is None or s > best[0]:
                best = (s, k + 1, f)
    med = statistics.median(mags) if mags else 1
    s, k, f = best
    amp = 2 * s / (ROWS * n)                       # sinusoid peak amplitude, fraction of luma
    # Drift-independent strength: per frame, RMS amplitude of the residual's k = 1..8 row components
    # (time-varying part only, the static scene was removed above); median over frames ignores
    # sporadic motion. Units: % of luma (sinusoid peak equivalent).
    per_frame = [math.sqrt(sum(abs(ck[k][i]) ** 2 for k in range(KMAX))) * 2 / ROWS for i in range(n)]
    return {"amplitudePct": round(100 * amp, 3), "bandsPerFrame": k, "temporalHz": round(abs(f), 2),
            "direction": "up" if f > 0 else "down", "peakToMedian": round(s / med, 1) if med else None,
            "bandStrengthMedianPct": round(100 * statistics.median(per_frame), 3)}


def flash(g, t):
    n = len(g)
    med = statistics.median(g)
    h = [x / med for x in g]
    jumps = 0
    for i in range(n):
        w = h[max(0, i - 15):i + 16]
        if abs(h[i] - statistics.median(w)) > 0.04:
            jumps += 1
    span = t[-1] - t[0] if n > 1 else 1
    fps = (n - 1) / span if span > 0 else 30
    hm = sum(h) / n
    best = (0, 0)
    df = max(0.05, 1 / span)
    for j in range(int(0.5 / df), int(fps / 2 / df) + 1):
        f = j * df
        s = abs(sum((x - hm) * cmath.exp(-2j * math.pi * f * ti) for x, ti in zip(h, t)))
        if s > best[0]:
            best = (s, f)
    return {"framesWithJump4pct": jumps, "lumaFlickerPct": round(100 * 2 * best[0] / n, 3), "lumaFlickerHz": round(best[1], 2),
            "lumaRangePct": round(100 * (max(h) - min(h)), 2)}


def noise_and_blocks(path, seconds):
    rc, out, _ = _run([FFMPEG, "-nostdin", "-v", "error", "-t", str(seconds), "-i", path, "-vf",
                       "signalstats,blockdetect=period_min=8:period_max=8,metadata=print:file=-", "-an", "-f", "null", "-"],
                      max(300, 4 * seconds))
    ydif, blk = [], []
    for line in out.splitlines():
        if "lavfi.signalstats.YDIF=" in line: ydif.append(float(line.split("=")[1]))
        elif "lavfi.block=" in line: blk.append(float(line.split("=")[1]))
    return {"temporalNoiseYdifMedian": round(statistics.median(ydif[1:]), 3) if len(ydif) > 2 else None,
            "blockinessMedian": round(statistics.median(blk), 3) if blk else None}


def audit(path, seconds=60):
    # -fps_mode passthrough: one output row-profile per real frame (the default CFR mode duplicates or
    # drops frames for VFR camera files, which breaks the frame <-> PTS pairing the analysis relies on).
    rc, raw, err = _run([FFMPEG, "-nostdin", "-v", "error", "-i", path, "-t", str(seconds), "-fps_mode", "passthrough", "-vf",
                         f"extractplanes=y,scale=1:{ROWS}:flags=area:in_range=full:out_range=full,format=gray16le", "-f", "rawvideo", "-pix_fmt", "gray16le", "-"],
                        max(300, 4 * seconds), binary=True)
    n = len(raw) // (2 * ROWS)
    if n < 30:
        return {"file": os.path.basename(path), "error": f"too few frames ({n}) {err[:200]}"}
    t = _pts(path, n)
    if len(t) < 30:
        return {"file": os.path.basename(path), "error": "no packet timestamps"}
    if len(t) != n:                      # never invent a clock: analyse only frames with a real PTS
        n = min(n, len(t)); t = t[:n]
    # ffmpeg's gray16 conversion returns 65535/219*(Y-16) for limited AND full range input; for full-range
    # video (black = 0) add the offset back so the profile is proportional to the luma above black.
    rc2, info, _ = _run([FFPROBE, "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=pix_fmt,color_range",
                         "-of", "csv=p=0", path], 60)
    pf, cr = (info.strip() + ",,").split(",")[:2]
    black = 16 * 65535 / 219 if (cr.strip() in ("pc", "full") or pf.startswith("yuvj")) else 0.0
    prof = [tuple(v + black for v in struct.unpack_from(f"<{ROWS}H", raw, i * 2 * ROWS)) for i in range(n)]
    g = [sum(p) / ROWS for p in prof]
    out = {"file": os.path.basename(path), "framesAnalysed": n, "seconds": round(t[-1], 2)}
    out["rollingBands"] = rolling_bands(prof, t, g)
    out["flash"] = flash(g, t)
    out.update(noise_and_blocks(path, min(seconds, 30)))
    if KYMO:
        kymograph(prof, g, KYMO)
        out["kymograph"] = os.path.basename(KYMO)
    return out


def kymograph(prof, g, png, frames=480):
    """Rows (y) x frames (x) of the brightness residual: drifting bands appear as diagonal stripes.
    Contrast is stretched to +-1 % of luma (mid-grey = no deviation) so sub-percent bands are visible."""
    n = min(frames, len(prof))
    m = [sum(prof[i][y] for i in range(len(prof))) / len(prof) for y in range(ROWS)]
    gm = sum(m) / ROWS
    pix = bytearray()
    for y in range(ROWS):
        for i in range(n):
            r = prof[i][y] / g[i] - m[y] / gm
            pix.append(max(0, min(255, int(128 + 127 * r / 0.01))))
    pgm = png + ".pgm"
    with open(pgm, "wb") as f:
        f.write(f"P5 {n} {ROWS} 255\n".encode()); f.write(pix)
    _run([FFMPEG, "-nostdin", "-v", "error", "-y", "-i", pgm, "-vf", "scale=iw*2:ih*2:flags=neighbor", png], 60)
    os.remove(pgm)


if __name__ == "__main__":
    secs = int(sys.argv[sys.argv.index("--seconds") + 1]) if "--seconds" in sys.argv else 60
    KYMO = sys.argv[sys.argv.index("--kymo") + 1] if "--kymo" in sys.argv else None
    print(json.dumps(audit(sys.argv[1], secs), indent=1))
