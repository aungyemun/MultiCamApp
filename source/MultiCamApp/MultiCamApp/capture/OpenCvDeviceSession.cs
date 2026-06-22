using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>Tracks DirectShow devices already opened by another slot (multi-camera).</summary>
public static class OpenCvDeviceSession
{
    private static readonly LogService Log = new();
    // touched to ensure file is saved
    private static readonly object Gate = new();
    private static readonly HashSet<int> UsedIndices = new();
    private static readonly HashSet<string> UsedNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> UsedUris = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, OpenCvDeviceBinding> BindingsByDeviceId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, int> IndexOwnerBySlot = new();
    private static readonly Dictionary<string, int> NameOwnerBySlot = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> UriOwnerBySlot = new(StringComparer.OrdinalIgnoreCase);

    public readonly record struct BindingConflict(bool Conflict, string KeyType, string KeyValue, int? OwnerSlot);

    public static void Reset()
    {
        lock (Gate)
        {
            UsedIndices.Clear();
            UsedNames.Clear();
            UsedUris.Clear();
            BindingsByDeviceId.Clear();
            IndexOwnerBySlot.Clear();
            NameOwnerBySlot.Clear();
            UriOwnerBySlot.Clear();
        }
        SlotDeviceOwnership.Reset();
    }

    /// <summary>
    /// Clear only active runtime claims (used indices/names/uris and slot ownership)
    /// without discarding remembered BindingsByDeviceId which represent cached device->index mappings.
    /// Use this before Start Preview to remove leftover runtime claims while preserving mapping cache.
    /// </summary>
    public static void ClearActiveClaims()
    {
        lock (Gate)
        {
            UsedIndices.Clear();
            UsedNames.Clear();
            UsedUris.Clear();
            IndexOwnerBySlot.Clear();
            NameOwnerBySlot.Clear();
            UriOwnerBySlot.Clear();
        }
        SlotDeviceOwnership.Reset();
    }

    public static IReadOnlyDictionary<string, OpenCvDeviceBinding> DumpRememberedBindings()
    {
        lock (Gate)
        {
            return new Dictionary<string, OpenCvDeviceBinding>(BindingsByDeviceId);
        }
    }

    public static void ClearRememberedBindings()
    {
        lock (Gate)
            BindingsByDeviceId.Clear();
    }

    public static void RememberDevice(string deviceId, OpenCvDeviceBinding binding)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        lock (Gate)
            BindingsByDeviceId[deviceId] = binding;
    }

    public static void ForgetDevice(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        lock (Gate)
            BindingsByDeviceId.Remove(deviceId);
    }

    public static bool TryGetRememberedBinding(string deviceId, out OpenCvDeviceBinding binding)
    {
        lock (Gate)
        {
            if (BindingsByDeviceId.TryGetValue(deviceId, out binding))
                return !IsBindingInUse(binding);
        }

        binding = default;
        return false;
    }

    public static bool IsBindingTaken(OpenCvDeviceBinding binding) => IsBindingInUse(binding);

    public static BindingConflict DetectConflict(OpenCvDeviceBinding binding, int currentSlot)
    {
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(binding.DirectShowOpenUri)
                && UriOwnerBySlot.TryGetValue(binding.DirectShowOpenUri, out var uriOwner)
                && uriOwner != currentSlot)
            {
                return new BindingConflict(true, "uri", binding.DirectShowOpenUri, uriOwner);
            }

            if (binding.Index >= 0
                && IndexOwnerBySlot.TryGetValue(binding.Index, out var indexOwner)
                && indexOwner != currentSlot)
            {
                return new BindingConflict(true, "index", binding.Index.ToString(), indexOwner);
            }

            // Friendly name can be duplicated across physical devices; do not treat as a hard conflict.
            if (binding.Index < 0
                && string.IsNullOrWhiteSpace(binding.DirectShowOpenUri)
                && !string.IsNullOrWhiteSpace(binding.DirectShowName)
                && NameOwnerBySlot.TryGetValue(binding.DirectShowName, out var nameOwner)
                && nameOwner != currentSlot)
            {
                return new BindingConflict(true, "name", binding.DirectShowName, nameOwner);
            }
        }

        return new BindingConflict(false, "", "", null);
    }

    private static bool IsBindingInUse(OpenCvDeviceBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.DirectShowOpenUri)
            && UsedUris.Contains(binding.DirectShowOpenUri))
        {
            Log.Info("camera", $"OpenCvDeviceSession: binding in use by uri {binding.DirectShowOpenUri}");
            return true;
        }

        if (binding.Index >= 0 && UsedIndices.Contains(binding.Index))
        {
            Log.Info("camera", $"OpenCvDeviceSession: binding in use by index {binding.Index}");
            return true;
        }

        // Friendly name is only a fallback uniqueness key when neither URI nor index are available.
        if (binding.Index < 0
            && string.IsNullOrWhiteSpace(binding.DirectShowOpenUri)
            && !string.IsNullOrWhiteSpace(binding.DirectShowName)
            && UsedNames.Contains(binding.DirectShowName))
        {
            Log.Info("camera", $"OpenCvDeviceSession: binding in use by name {binding.DirectShowName}");
            return true;
        }

        return false;
    }

    public static void Claim(OpenCvDeviceBinding binding)
    {
        Claim(slotIndex: -1, binding);
    }

    public static void Claim(int slotIndex, OpenCvDeviceBinding binding)
    {
        lock (Gate)
        {
            if (binding.Index >= 0)
            {
                UsedIndices.Add(binding.Index);
                if (slotIndex >= 0)
                    IndexOwnerBySlot[binding.Index] = slotIndex;
            }

            if (!string.IsNullOrWhiteSpace(binding.DirectShowName))
            {
                UsedNames.Add(binding.DirectShowName);
                if (slotIndex >= 0)
                    NameOwnerBySlot[binding.DirectShowName] = slotIndex;
            }

            if (!string.IsNullOrWhiteSpace(binding.DirectShowOpenUri))
            {
                UsedUris.Add(binding.DirectShowOpenUri);
                if (slotIndex >= 0)
                    UriOwnerBySlot[binding.DirectShowOpenUri] = slotIndex;
            }
        }
    }

    public static void Release(OpenCvDeviceBinding? binding)
    {
        Release(slotIndex: -1, binding);
    }

    public static void Release(int slotIndex, OpenCvDeviceBinding? binding)
    {
        if (binding is not { } b) return;
        lock (Gate)
        {
            if (b.Index >= 0)
            {
                UsedIndices.Remove(b.Index);
                if (slotIndex < 0
                    || (IndexOwnerBySlot.TryGetValue(b.Index, out var owner) && owner == slotIndex))
                {
                    IndexOwnerBySlot.Remove(b.Index);
                }
            }
            if (!string.IsNullOrWhiteSpace(b.DirectShowName))
            {
                UsedNames.Remove(b.DirectShowName);
                if (slotIndex < 0
                    || (NameOwnerBySlot.TryGetValue(b.DirectShowName, out var owner) && owner == slotIndex))
                {
                    NameOwnerBySlot.Remove(b.DirectShowName);
                }
            }
            if (!string.IsNullOrWhiteSpace(b.DirectShowOpenUri))
            {
                UsedUris.Remove(b.DirectShowOpenUri);
                if (slotIndex < 0
                    || (UriOwnerBySlot.TryGetValue(b.DirectShowOpenUri, out var owner) && owner == slotIndex))
                {
                    UriOwnerBySlot.Remove(b.DirectShowOpenUri);
                }
            }
        }
    }

    internal static bool IsNameTaken(string name)
    {
        lock (Gate)
            return UsedNames.Contains(name);
    }

    internal static bool IsIndexTaken(int index)
    {
        lock (Gate)
            return UsedIndices.Contains(index);
    }
}
