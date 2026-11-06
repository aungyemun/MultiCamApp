"""Independent analysis of a MultiCamApp session folder (test harness; stdlib only).
Usage: python analyze.py <session_dir> <expected_w> <expected_h> <expected_fps> [--quality] [--snap OUTDIR]
Writes <session_dir>\\_autotest_analysis.json and prints a summary."""
import csv, glob, hashlib, json, os, statistics, subprocess, sys
FF = os.environ.get("MCA_FFMPEG_DIR") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "runtime", "ffmpeg")
FFPROBE, FFMPEG = os.path.join(FF, "ffprobe.exe"), os.path.join(FF, "ffmpeg.exe")

TIMEOUTS = []  # steps that hit their limit (reported in the JSON instead of crashing the whole analysis)

def run(args, timeout=300):
    """Run a tool with a hard timeout; stdin is closed so ffmpeg can never block waiting for input.
    subprocess.run kills the child on expiry; we record it and return rc=-9."""
    try:
        p = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace",
                           timeout=timeout, stdin=subprocess.DEVNULL)
        return p.returncode, p.stdout, p.stderr
    except subprocess.TimeoutExpired:
        TIMEOUTS.append(f"{os.path.basename(args[0])} {' '.join(str(a) for a in args[1:4])}... >{timeout}s")
        return -9, "", f"TIMEOUT after {timeout}s"

def stats(xs):
    if not xs: return None
    s = sorted(xs)
    pct = lambda q: s[min(len(s) - 1, int(q * (len(s) - 1) + 0.5))]
    return {"n": len(xs), "mean": statistics.fmean(xs), "sd": statistics.pstdev(xs) if len(xs) > 1 else 0.0,
            "min": s[0], "p1": pct(0.01), "median": pct(0.5), "p99": pct(0.99), "max": s[-1]}

def flatten(d, prefix=""):
    out = {}
    if isinstance(d, dict):
        for k, v in d.items(): out.update(flatten(v, f"{prefix}{k}."))
    elif isinstance(d, list):
        for i, v in enumerate(d[:8]): out.update(flatten(v, f"{prefix}{i}."))
    else: out[prefix[:-1]] = d
    return out

def analyze_video(path, exp_w, exp_h, exp_fps, quality, snapdir, tag):
    r = {"file": os.path.basename(path), "sizeBytes": os.path.getsize(path)}
    rc, out, err = run([FFPROBE, "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path])
    if rc != 0: r["probeError"] = err.strip()[:300]; return r
    j = json.loads(out); vs = next(s for s in j["streams"] if s["codec_type"] == "video")
    fr = lambda x: (lambda a, b: float(a) / float(b) if float(b) else 0)(*x.split("/"))
    r.update(codec=vs.get("codec_name"), profile=vs.get("profile"), pix_fmt=vs.get("pix_fmt"), width=vs["width"], height=vs["height"],
             sar=vs.get("sample_aspect_ratio"), dar=vs.get("display_aspect_ratio"), color_range=vs.get("color_range"),
             color_space=vs.get("color_space"), color_primaries=vs.get("color_primaries"), color_transfer=vs.get("color_transfer"),
             avg_frame_rate=fr(vs["avg_frame_rate"]), r_frame_rate=vs["r_frame_rate"], nb_frames_header=int(vs.get("nb_frames", 0) or 0),
             duration=float(j["format"].get("duration", 0)), bitrateKbps=round(int(j["format"].get("bit_rate", 0)) / 1000), audio=any(s["codec_type"] == "audio" for s in j["streams"]))
    rc, out, err = run([FFPROBE, "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time,flags", "-of", "csv=p=0", path])
    pts = sorted(float(l.split(",")[0]) for l in out.splitlines() if l and l.split(",")[0] not in ("", "N/A"))
    keyframes = sum(1 for l in out.splitlines() if ",K" in l)
    iv = [(b - a) * 1000 for a, b in zip(pts, pts[1:])]
    nominal = 1000.0 / exp_fps
    r.update(packets=len(pts), keyframes=keyframes, ptsSpanSec=(pts[-1] - pts[0]) if pts else 0,
             fpsFromPts=(len(pts) - 1) / (pts[-1] - pts[0]) if len(pts) > 1 and pts[-1] > pts[0] else 0,
             ptsIntervalMs=stats(iv), gapsOver1_5x=sum(1 for x in iv if x > 1.5 * nominal), gapsOver2_5x=sum(1 for x in iv if x > 2.5 * nominal),
             nonMonotonicOrZero=sum(1 for x in iv if x <= 0))
    # exact duplicate decoded frames (framemd5, passthrough = no frame rate conversion)
    dyn = int(max(300, 1.5 * r["duration"]))  # full-decode steps: 1.5x real time, at least 5 min
    rc, out, err = run([FFMPEG, "-nostdin", "-v", "error", "-i", path, "-map", "0:v:0", "-fps_mode", "passthrough", "-f", "framemd5", "-"], timeout=dyn)
    hashes = [l.split(",")[-1].strip() for l in out.splitlines() if l and not l.startswith("#")]
    dup = sum(1 for a, b in zip(hashes, hashes[1:]) if a == b)
    r.update(decodedFrames=len(hashes), consecutiveDuplicateFrames=dup, decodeErrors=err.strip()[:300])
    r["matchesExpected"] = {"resolution": (r["width"], r["height"]) == (exp_w, exp_h),
                            "fpsWithin1": abs(r["fpsFromPts"] - exp_fps) <= 1.0,
                            "colorTagsBt709Pc": (r["color_range"], r["color_space"], r["color_primaries"]) == ("pc", "bt709", "bt709"),
                            "squarePixels": r["sar"] in (None, "1:1")}
    if quality:
        # Blur/luma: sample ~3000 frames max (full-frame blurdetect on a 10-min 720p file took ~6 min/camera).
        # Freeze/black detection below still decodes every frame, because it depends on consecutive frames.
        n_every = max(1, -(-r["decodedFrames"] // 3000))
        r["qualitySampledEveryNthFrame"] = n_every
        sel = f"select='not(mod(n\\,{n_every}))'," if n_every > 1 else ""
        rc, out, err = run([FFMPEG, "-nostdin", "-v", "info", "-i", path, "-vf",
                            sel + "blurdetect=block_width=32:block_height=32,signalstats,metadata=print:file=-",
                            "-an", "-f", "null", "-"], timeout=dyn)
        blur, yavg = [], []
        for l in out.splitlines():
            if "lavfi.blur=" in l: blur.append(float(l.split("=")[1]))
            elif "lavfi.signalstats.YAVG=" in l: yavg.append(float(l.split("=")[1]))
        r["blur"] = stats(blur); r["lumaYavg"] = stats(yavg)
        rc, out, err = run([FFMPEG, "-nostdin", "-v", "info", "-i", path, "-vf", "freezedetect=n=0.001:d=0.5,blackdetect=d=0.5:pix_th=0.05", "-an", "-f", "null", "-"], timeout=dyn)
        r["freezeEvents"] = sum(1 for l in err.splitlines() if "freeze_start" in l)
        r["blackEvents"] = sum(1 for l in err.splitlines() if "black_start" in l)
    if snapdir:
        os.makedirs(snapdir, exist_ok=True)
        mid = max(0.0, r["duration"] / 2)
        run([FFMPEG, "-nostdin", "-v", "error", "-y", "-ss", f"{mid:.2f}", "-i", path, "-frames:v", "1", os.path.join(snapdir, f"{tag}_mid.png")])
        run([FFMPEG, "-nostdin", "-v", "error", "-y", "-ss", f"{mid:.2f}", "-i", path, "-frames:v", "1", "-vf", "crop=iw/3:ih/3:iw/3:ih/3", os.path.join(snapdir, f"{tag}_mid_crop100.png")])
    return r

def analyze_csv(path, exp_fps):
    r = {"file": os.path.basename(path)}
    with open(path, newline="", encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    r["rows"] = len(rows); r["columns"] = list(rows[0].keys()) if rows else []
    t = [float(x["appTimestampMsFromRecStart"]) for x in rows if x.get("appTimestampMsFromRecStart")]
    iv = [b - a for a, b in zip(t, t[1:])]
    r["appIntervalMs"] = stats(iv)
    r["appSpanSec"] = (t[-1] - t[0]) / 1000 if len(t) > 1 else 0
    r["fpsFromCsv"] = (len(t) - 1) / r["appSpanSec"] if r["appSpanSec"] > 0 else 0
    r["nonMonotonic"] = sum(1 for x in iv if x <= 0)
    r["firstRowUtc"] = rows[0].get("captureTimestampUtc") if rows else None
    r["lastRowUtc"] = rows[-1].get("captureTimestampUtc") if rows else None
    fi = [int(x["frameIndex"]) for x in rows if x.get("frameIndex", "").isdigit()]
    r["frameIndexFirst"] = fi[0] if fi else None
    r["frameIndexGaps"] = sum(1 for a, b in zip(fi, fi[1:]) if b != a + 1)
    r["droppedWarnings"] = sum(1 for x in rows if (x.get("droppedFrameWarning") or "").strip().lower() not in ("", "0", "false", "no"))
    ecf = [float(x["estimatedCaptureFps"]) for x in rows if x.get("estimatedCaptureFps") not in (None, "")]
    r["estimatedCaptureFpsLast"] = ecf[-1] if ecf else None
    return r

def main():
    sess, w, h, fps = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), float(sys.argv[4])
    quality = "--quality" in sys.argv
    snap = sys.argv[sys.argv.index("--snap") + 1] if "--snap" in sys.argv else None
    out = {"session": os.path.basename(sess), "expected": {"w": w, "h": h, "fps": fps}, "cameras": {}}
    out["files"] = sorted(os.path.relpath(p, sess) for p in glob.glob(os.path.join(sess, "**", "*"), recursive=True) if os.path.isfile(p))
    for cam in sorted(d for d in os.listdir(sess) if os.path.isdir(os.path.join(sess, d)) and d.lower().startswith("cam")):
        cd = os.path.join(sess, cam); c = {}
        mp4 = [p for p in glob.glob(os.path.join(cd, "*.mp4")) if not p.endswith(".tmp.mp4")]
        c["tmpFilesLeft"] = [os.path.basename(p) for p in glob.glob(os.path.join(cd, "*.tmp.mp4"))]
        if mp4: c["video"] = analyze_video(mp4[0], w, h, fps, quality, snap, f"{out['session']}_{cam}")
        csvs = glob.glob(os.path.join(cd, "*timestamps*.csv"))
        if csvs: c["csv"] = analyze_csv(csvs[0], fps)
        mj = [p for p in glob.glob(os.path.join(cd, "*metadata.json"))]
        if mj:
            flat = flatten(json.load(open(mj[0], encoding="utf-8-sig")))
            keys = ("resolution", "width", "height", "fps", "frame", "verdict", "result", "status", "mismatch", "format", "duration", "device", "exposure", "focus", "flicker", "warning")
            c["metadata"] = {k: v for k, v in flat.items() if any(s in k.lower() for s in keys)}
            c["metadataPrivacyLeaks"] = [k for k, v in flat.items() if isinstance(v, str) and (":\\" in v or "\\\\" in v or os.environ.get("USERNAME", "~~").lower() in v.lower())]
        if "video" in c and "csv" in c:
            c["csvRowsMinusVideoFrames"] = c["csv"]["rows"] - c["video"]["decodedFrames"]
        out["cameras"][cam] = c
    cams = list(out["cameras"].values())
    if len(cams) >= 2 and all("video" in c for c in cams):
        from datetime import datetime
        def ts(s):
            try: return datetime.fromisoformat(s.replace("Z", "+00:00")).timestamp()
            except Exception: return None
        firsts = [ts(c.get("csv", {}).get("firstRowUtc") or "") for c in cams]
        lasts = [ts(c.get("csv", {}).get("lastRowUtc") or "") for c in cams]
        out["interCamera"] = {
            "frameCounts": [c["video"]["decodedFrames"] for c in cams],
            "durations": [round(c["video"]["duration"], 3) for c in cams],
            "csvFirstRowSpreadMs": (max(firsts) - min(firsts)) * 1000 if None not in firsts else None,
            "csvLastRowSpreadMs": (max(lasts) - min(lasts)) * 1000 if None not in lasts else None,
        }
        h0 = [hashlib.md5(open(c["video"]["file"] and os.path.join(sess, cam, c["video"]["file"]), "rb").read(1 << 20)).hexdigest() for cam, c in out["cameras"].items()]
        out["interCamera"]["distinctFiles"] = len(set(h0)) == len(h0)
    out["timedOutSteps"] = TIMEOUTS
    json.dump(out, open(os.path.join(sess, "_autotest_analysis.json"), "w", encoding="utf-8"), indent=1, default=str)
    print(json.dumps(out, indent=1, default=str))

if __name__ == "__main__":
    main()
