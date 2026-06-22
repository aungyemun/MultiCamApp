////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Capture;

/// <summary>Ensures each WinRT device ID is opened on at most one camera slot (cam1–cam4).</summary>
public static class SlotDeviceOwnership
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Owners = new(StringComparer.OrdinalIgnoreCase);

    public static void Reset()
    {
        lock (Gate)
            Owners.Clear();
    }

    public static bool IsOwnedByOtherSlot(string deviceId, int slotIndex)
    {
        lock (Gate)
            return Owners.TryGetValue(deviceId, out var owner) && owner != slotIndex;
    }

    public static int? GetOwnerSlot(string deviceId)
    {
        lock (Gate)
            return Owners.TryGetValue(deviceId, out var slot) ? slot : null;
    }

    /// <summary>Registers device for slot. Clears any prior device registered on the same slot.</summary>
    public static bool TryAssign(string deviceId, int slotIndex)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        lock (Gate)
        {
            if (Owners.TryGetValue(deviceId, out var owner) && owner != slotIndex)
                return false;

            foreach (var key in Owners.Where(kv => kv.Value == slotIndex).Select(kv => kv.Key).ToList())
                Owners.Remove(key);

            Owners[deviceId] = slotIndex;
            return true;
        }
    }

    public static void Release(string? deviceId, int slotIndex)
    {
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(deviceId)
                && Owners.TryGetValue(deviceId, out var owner)
                && owner == slotIndex)
                Owners.Remove(deviceId);
        }
    }

    public static void ReleaseSlot(int slotIndex)
    {
        lock (Gate)
        {
            foreach (var key in Owners.Where(kv => kv.Value == slotIndex).Select(kv => kv.Key).ToList())
                Owners.Remove(key);
        }
    }
}
