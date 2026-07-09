////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////

#warning STABLE_CORE_V2: Stable core frozen (VideoEngineV2 recording engine, native metadata, video verification, session comparison). See docs/STABLE_CORE_V2_FREEZE.md and docs/STABLE_CORE_V2_REGRESSION_CHECKLIST.md before modifying protected code.

namespace MultiCamApp.Core;

/// <summary>
/// Compile-time marker for STABLE_CORE_V2 (MultiCamApp v2.0.0 build 333, first stable release).
/// Supersedes STABLE_CORE_V1 (see <see cref="StableCoreV1"/>) for the files it protects — the
/// actively-used VideoEngineV2 recording pipeline and native V2-aware verification/session
/// comparison logic, none of which existed or was schema-aware when STABLE_CORE_V1 was declared.
/// </summary>
public static class StableCoreV2
{
    public const string Id = "STABLE_CORE_V2";
    public const string FreezeDate = "2026-07-10";
    public const string ValidatedAppVersion = "2.0.0";
    public const int ValidatedBuild = 333;
}
