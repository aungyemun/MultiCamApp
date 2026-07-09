////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// Process-wide, ref-counted Media Foundation platform lifetime, shared by every
// MediaFoundationEncoderService instance (up to 4 concurrent camera slots each own one).
// MFStartup/MFShutdown calls must be balanced; a naive per-instance pair would shut down the
// shared MF platform state out from under sibling cameras still recording.

using MultiCamApp.Utils;
using Vortice.MediaFoundation;

namespace MultiCamApp.Capture.VideoEngineV2;

internal static class MediaFoundationRuntime
{
    private static readonly object s_lock = new();
    private static int s_refCount;

    public static void AddRef()
    {
        lock (s_lock)
        {
            if (s_refCount == 0)
            {
                MediaFactory.MFStartup().CheckError();
                AppDiagnosticLogger.Runtime("MF_RUNTIME_STARTUP");
            }
            s_refCount++;
        }
    }

    public static void Release()
    {
        lock (s_lock)
        {
            if (s_refCount == 0) return;
            s_refCount--;
            if (s_refCount == 0)
            {
                try { MediaFactory.MFShutdown(); } catch { /* best-effort on app shutdown */ }
                AppDiagnosticLogger.Runtime("MF_RUNTIME_SHUTDOWN");
            }
        }
    }
}
