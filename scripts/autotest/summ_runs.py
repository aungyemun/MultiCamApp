"""Compact summary of runs\<name>_analysis.json + _run.json (test harness). Usage: python summ_runs.py <prefix>..."""
import glob, json, os, sys
R = os.environ.get("MCA_RUNS_DIR") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "temp", "autotest", "runs")
for pre in sys.argv[1:]:
    for f in sorted(glob.glob(os.path.join(R, pre + "*_analysis.json"))):
        n = os.path.basename(f)[:-len("_analysis.json")]
        d = json.load(open(f, encoding="utf-8"))
        try: run = json.load(open(os.path.join(R, n + "_run.json"), encoding="utf-8-sig"))
        except Exception: run = {}
        print(f"== {n}  preview {run.get('previewReadySec')}s  rec-start {run.get('recordStartUiSec')}s  stop {run.get('recordStopUiSec')}s  RAM max {run.get('ramMB', {}).get('max')} MB  timeouts={d.get('timedOutSteps')}")
        if run.get("lockStatus"): print("   lock:", run["lockStatus"])
        for cam, c in d["cameras"].items():
            v, s = c.get("video", {}), c.get("csv", {})
            md = c.get("metadata", {})
            dev = next((md[k] for k in md if k.endswith("device") or k.endswith("deviceName")), "")
            fb = next((md[k] for k in md if "fallbackused" in k.lower().replace("_", "")), "")
            print(f"   {cam} {str(dev)[:26]:26} {v.get('width')}x{v.get('height')} {v.get('fpsFromPts', 0):6.2f}fps frames={v.get('decodedFrames')} csvDiff={c.get('csvRowsMinusVideoFrames')} "
                  f"dup={v.get('consecutiveDuplicateFrames')} frz={v.get('freezeEvents')} blk={v.get('blackEvents')} gaps>1.5x={v.get('gapsOver1_5x')} >2.5x={v.get('gapsOver2_5x')} "
                  f"{v.get('bitrateKbps')}kbps blur={round((v.get('blur') or {}).get('mean', 0), 2)} fallback={fb} match={all((v.get('matchesExpected') or {}).values())} leaks={c.get('metadataPrivacyLeaks')}")
        ic = d.get("interCamera")
        if ic: print("   inter:", {k: (round(x, 1) if isinstance(x, float) else x) for k, x in ic.items()})
