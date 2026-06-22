////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////

#warning STABLE_CORE_V1: Stable core frozen (recording, metadata, verification, session comparison). See docs/STABLE_CORE_V1_FREEZE.md and docs/STABLE_CORE_V1_REGRESSION_CHECKLIST.md before modifying protected code.

namespace MultiCamApp.Core;

/// <summary>
/// Compile-time marker for STABLE_CORE_V1 (MultiCamApp v1.0.36 build 136).
/// </summary>
public static class StableCoreV1
{
    public const string Id = "STABLE_CORE_V1";
    public const string FreezeDate = "2026-06-11";
    public const string ValidatedAppVersion = "1.0.36";
    public const int ValidatedBuild = 136;
}
