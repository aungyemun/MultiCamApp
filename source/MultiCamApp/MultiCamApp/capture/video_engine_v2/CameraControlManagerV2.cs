////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

using MultiCamApp.Utils;
using Windows.Media.Capture;
using Windows.Media.Devices;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Manages scientific camera controls via <see cref="VideoDeviceController"/> on an
/// already-open <see cref="MediaCapture"/> session.
/// Applies research-safe defaults: autofocus off, low-light compensation off,
/// optical image stabilisation off.
/// Each control is queried for support, applied if supported, read back, and reported.
/// </summary>
public sealed class CameraControlManagerV2 : IDisposable
{
    private VideoDeviceController? _controller;
    private bool _disposed;

    /// <summary>Raised after any control is applied, with the readback result.</summary>
    public event EventHandler<V2ControlApplyResult>? ControlApplied;

    public bool IsAttached => _controller is not null;

    /// <summary>Snapshot of all applied control statuses from the last <see cref="ApplyResearchDefaultsAsync"/>.</summary>
    public IReadOnlyList<V2ControlApplyResult> LastAppliedResults { get; private set; }
        = Array.Empty<V2ControlApplyResult>();

    /// <summary>Warning message if Windows Studio Effects may be active.</summary>
    public string? WindowsStudioEffectsWarning { get; private set; }

    // ── Attach / Detach ───────────────────────────────────────────────────────

    public Task AttachAsync(MediaCapture mediaCapture, CancellationToken ct = default)
    {
        _controller = mediaCapture.VideoDeviceController;
        return Task.CompletedTask;
    }

    public void Detach() => _controller = null;

    // ── Research-safe defaults ────────────────────────────────────────────────

    public Task<IReadOnlyList<V2ControlApplyResult>> ApplyResearchDefaultsAsync(
        CancellationToken ct = default)
        => ApplyResearchDefaultsInternalAsync(ct);

    private async Task<IReadOnlyList<V2ControlApplyResult>> ApplyResearchDefaultsInternalAsync(
        CancellationToken ct)
    {
        if (_controller is null)
            return Array.Empty<V2ControlApplyResult>();

        var results = new List<V2ControlApplyResult>();

        if (VideoEngineSettings.DisableAutoFocus)
            results.Add(await DisableFocusAsync(ct).ConfigureAwait(false));

        if (VideoEngineSettings.DisableAutoExposure)
            results.Add(await DisableAutoExposureAsync(ct).ConfigureAwait(false));

        // Only disable low-light compensation when exposure is actually controllable
        // (ExposureControl.Supported). When exposure can't be touched by the app at all,
        // the camera's onboard auto-exposure runs uncontrolled regardless — disabling LLC
        // in that case only darkens the picture without buying any recording reproducibility.
        if (_controller.ExposureControl.Supported)
        {
            results.Add(await SetLowLightCompensationAsync(enabled: false, ct).ConfigureAwait(false));
        }
        else
        {
            results.Add(new V2ControlApplyResult
            {
                Control        = V2CameraControl.LowLightCompensation,
                Applied        = false,
                ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
                WarningMessage = "Skipped — exposure control unsupported on this device, so disabling " +
                    "low-light compensation would only darken the image with no reproducibility benefit.",
            });
        }
        results.Add(await SetOpticalStabilisationAsync(enabled: false, ct).ConfigureAwait(false));

        if (VideoEngineSettings.EnableFlickerReduction)
            results.Add(await SetFlickerReductionAutoAsync(ct).ConfigureAwait(false));
        else
            results.Add(ProbeFlickerReduction());

        // Fix D: set white balance to Auto so the camera behaviour matches Windows Camera default.
        results.Add(await SetWhiteBalanceAutoAsync(ct).ConfigureAwait(false));

        CheckWindowsStudioEffects();

        foreach (var r in results) ControlApplied?.Invoke(this, r);

        // Persist control apply results to log for debugging
        foreach (var r in results)
        {
            string extra = r.Control == V2CameraControl.Exposure && r.ReadbackValue.HasValue
                ? $" shutter={r.ReadbackValue.Value * 1000:F2}ms(1/{(r.ReadbackValue.Value > 0 ? (1.0 / r.ReadbackValue.Value) : 0):F0}s)"
                : r.ReadbackValue.HasValue ? $" value={r.ReadbackValue.Value:F4}" : "";
            AppDiagnosticLogger.Runtime(
                $"V2_CONTROL control={r.Control} applied={r.Applied} " +
                $"readback={r.ReadbackStatus}{extra}" +
                (r.WarningMessage is not null ? $" warn={r.WarningMessage}" : ""));
        }

        LastAppliedResults = results.AsReadOnly();
        return LastAppliedResults;
    }

    // ── Individual controls ────────────────────────────────────────────────────

    /// <summary>Disables autofocus by setting manual focus mode.</summary>
    public async Task<V2ControlApplyResult> DisableFocusAsync(CancellationToken ct = default)
    {
        if (_controller is null) return NotAttached(V2CameraControl.Focus);

        var fc = _controller.FocusControl;
        if (!fc.Supported)
            return new V2ControlApplyResult
            {
                Control        = V2CameraControl.Focus,
                ReadbackStatus = V2ControlReadbackStatus.Unsupported,
            };

        bool applied = false;
        string? error = null;
        try
        {
            var modes = fc.SupportedFocusModes;
            FocusMode targetMode = modes.Contains(FocusMode.Manual) ? FocusMode.Manual : FocusMode.Single;
            // FocusControl.Configure is synchronous — no await
            fc.Configure(new FocusSettings
            {
                Mode                  = targetMode,
                DisableDriverFallback = true,
            });
            applied = true;
        }
        catch (Exception ex) { error = ex.Message; }

        return new V2ControlApplyResult
        {
            Control        = V2CameraControl.Focus,
            Applied        = applied,
            ReadbackValue  = applied ? 0 : null,
            ReadbackStatus = applied ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Failed,
            WarningMessage = error,
        };
    }

    /// <summary>Enables or disables auto-exposure.</summary>
    public async Task<V2ControlApplyResult> SetExposureAsync(
        V2ExposureRequest request, CancellationToken ct = default)
    {
        if (_controller is null) return NotAttached(V2CameraControl.Exposure);

        var ec = _controller.ExposureControl;
        if (!ec.Supported)
            return new V2ControlApplyResult
            {
                Control        = V2CameraControl.Exposure,
                ReadbackStatus = V2ControlReadbackStatus.Unsupported,
            };

        bool applied = false;
        bool? autoReadback = null;
        string? error = null;
        try
        {
            // Correct WinRT method names: SetAutoAsync / SetValueAsync (no "Try" prefix)
            await ec.SetAutoAsync(request.AutoExposure).AsTask(ct);
            if (!request.AutoExposure && request.ManualValue.HasValue)
            {
                var duration = TimeSpan.FromSeconds(request.ManualValue.Value);
                await ec.SetValueAsync(duration).AsTask(ct);
            }
            applied = true;
        }
        catch (Exception ex) { error = ex.Message; }

        double? readback = null;
        try
        {
            if (ec.Supported)
            {
                autoReadback = ec.Auto;
                readback = ec.Value.TotalSeconds;
            }
        }
        catch { }

        var readbackStatus = !applied
            ? V2ControlReadbackStatus.Failed
            : autoReadback.HasValue && autoReadback.Value == request.AutoExposure
                ? V2ControlReadbackStatus.Confirmed
                : V2ControlReadbackStatus.Mismatch;

        return new V2ControlApplyResult
        {
            Control        = V2CameraControl.Exposure,
            Applied        = applied,
            ReadbackValue  = readback,
            ReadbackStatus = readbackStatus,
            WarningMessage = error,
        };
    }

    /// <summary>Short settle window given to the driver's own auto-exposure loop before it is
    /// frozen at camera open, so cameras with real <see cref="ExposureControl"/> support (e.g.
    /// OBSBOT-class devices) don't get locked to a dark transient read from the first instant
    /// after open. Cameras without ExposureControl support (most cheap UVC webcams) skip this
    /// delay entirely since the freeze call is a no-op for them anyway.</summary>
    private const int ExposureConvergenceDelayMs = 500;

    public async Task<V2ControlApplyResult> DisableAutoExposureAsync(CancellationToken ct = default)
    {
        if (_controller?.ExposureControl.Supported == true)
        {
            try { await _controller.ExposureControl.SetAutoAsync(true).AsTask(ct); } catch { }
            try { await Task.Delay(ExposureConvergenceDelayMs, ct); } catch (OperationCanceledException) { }
        }

        return await SetExposureAsync(new V2ExposureRequest { AutoExposure = false }, ct).ConfigureAwait(false);
    }

    public Task<V2ControlApplyResult> SetFocusAsync(V2FocusRequest request, CancellationToken ct = default)
        => request.AutoFocus
            ? Task.FromResult(V2ControlApplyResult.Skeleton(V2CameraControl.Focus))
            : DisableFocusAsync(ct);

    /// <summary>Sets backlight / low-light compensation on or off via <see cref="MediaDeviceControl"/>.</summary>
    public Task<V2ControlApplyResult> SetLowLightCompensationAsync(
        bool enabled, CancellationToken ct = default)
    {
        if (_controller is null) return Task.FromResult(NotAttached(V2CameraControl.LowLightCompensation));

        var ctrl = _controller.BacklightCompensation;
        if (!ctrl.Capabilities.Supported)
            return Task.FromResult(new V2ControlApplyResult
            {
                Control        = V2CameraControl.LowLightCompensation,
                ReadbackStatus = V2ControlReadbackStatus.Unsupported,
                WarningMessage = "BacklightCompensation not supported on this device.",
            });

        bool autoDisabled = ctrl.TrySetAuto(false);
        double offValue = Math.Clamp(0.0, ctrl.Capabilities.Min, ctrl.Capabilities.Max);
        bool valueApplied = ctrl.TrySetValue(enabled ? ctrl.Capabilities.Default : offValue);
        bool valueRead = ctrl.TryGetValue(out double readbackValue);
        bool confirmedOff = !enabled && valueRead
            && readbackValue <= offValue + Math.Max(ctrl.Capabilities.Step, 0.001);
        bool applied = valueApplied && (enabled || confirmedOff);

        return Task.FromResult(new V2ControlApplyResult
        {
            Control        = V2CameraControl.LowLightCompensation,
            Applied        = applied,
            ReadbackValue  = valueRead ? readbackValue : null,
            ReadbackStatus = applied ? V2ControlReadbackStatus.Confirmed
                : valueApplied ? V2ControlReadbackStatus.Mismatch : V2ControlReadbackStatus.Failed,
            WarningMessage = !autoDisabled && ctrl.Capabilities.AutoModeSupported
                ? "Could not disable automatic backlight compensation mode."
                : !valueApplied ? "Could not set backlight compensation to its off/minimum value." : null,
        });
    }

    // ── Capability probe ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads driver-reported capability ranges from the open VideoDeviceController.
    /// Must be called while the device is open (during or after preview).
    /// All TimeSpan exposure values are converted to seconds for the snapshot.
    /// </summary>
    public V2CameraCapabilitySnapshot ProbeCapabilities()
    {
        if (_controller is null)
            return new V2CameraCapabilitySnapshot { NotAttached = true };

        // Exposure
        var ec = _controller.ExposureControl;
        bool expSupported = ec.Supported;
        double expMin = 0, expMax = 0, expStep = 0, expCurrent = 0;
        if (expSupported)
        {
            try
            {
                expMin     = ec.Min.TotalSeconds;
                expMax     = ec.Max.TotalSeconds;
                expStep    = ec.Step.TotalSeconds;
                expCurrent = ec.Value.TotalSeconds;
            }
            catch { }
        }

        // Focus
        var fc = _controller.FocusControl;
        bool focSupported = fc.Supported;
        uint focMin = 0, focMax = 0, focStep = 0, focCurrent = 0;
        if (focSupported)
        {
            try
            {
                focMin     = fc.Min;
                focMax     = fc.Max;
                focStep    = fc.Step;
                focCurrent = fc.Value;
            }
            catch { }
        }

        // Backlight / low-light compensation
        bool llcSupported = _controller.BacklightCompensation.Capabilities.Supported;

        // White balance — Kelvin range
        var wbCtrl2 = _controller.WhiteBalanceControl;
        bool wbSupported = wbCtrl2.Supported;
        uint wbMin = 0, wbMax = 0, wbStep = 0, wbCurrent = 0;
        bool wbInAutoMode = false;
        if (wbSupported)
        {
            try
            {
                wbMin        = wbCtrl2.Min;
                wbMax        = wbCtrl2.Max;
                wbStep       = wbCtrl2.Step;
                wbCurrent    = wbCtrl2.Value;
                wbInAutoMode = wbCtrl2.Preset == ColorTemperaturePreset.Auto;
            }
            catch { }
        }

        // ISO / Analog Gain — locks digital noise floor alongside exposure
        var isoCtrl = _controller.IsoSpeedControl;
        bool isoSupported = isoCtrl.Supported;
        uint isoMin = 0, isoMax = 0, isoStep = 0, isoCurrent = 0;
        bool isoIsAuto = false;
        if (isoSupported)
        {
            try
            {
                isoMin     = isoCtrl.Min;
                isoMax     = isoCtrl.Max;
                isoStep    = isoCtrl.Step;
                isoCurrent = isoCtrl.Value;
                isoIsAuto  = isoCtrl.Auto;
            }
            catch { }
        }

        return new V2CameraCapabilitySnapshot
        {
            ExposureSupported  = expSupported,
            ExposureMinS       = expMin,
            ExposureMaxS       = expMax,
            ExposureStepS      = expStep,
            ExposureCurrentS   = expCurrent,
            FocusSupported     = focSupported,
            FocusMin           = focMin,
            FocusMax           = focMax,
            FocusStep          = focStep,
            FocusCurrent       = focCurrent,
            LowLightCompensationSupported = llcSupported,
            WhiteBalanceSupported  = wbSupported,
            WhiteBalanceMinK       = wbMin,
            WhiteBalanceMaxK       = wbMax,
            WhiteBalanceStepK      = wbStep,
            WhiteBalanceCurrentK   = wbCurrent,
            WhiteBalanceInAutoMode = wbInAutoMode,
            IsoSupported           = isoSupported,
            IsoMin                 = isoMin,
            IsoMax                 = isoMax,
            IsoStep                = isoStep,
            IsoCurrent             = isoCurrent,
            IsoIsAuto              = isoIsAuto,
        };
    }

    /// <summary>Enables or disables optical image stabilisation (OIS / OISC).</summary>
    public Task<V2ControlApplyResult> SetOpticalStabilisationAsync(
        bool enabled, CancellationToken ct = default)
    {
        if (_controller is null) return Task.FromResult(NotAttached(V2CameraControl.OpticalStabilization));

        // Correct property name: OpticalImageStabilizationControl (not OpticalImageStabilization)
        var ois = _controller.OpticalImageStabilizationControl;
        if (!ois.Supported)
            return Task.FromResult(new V2ControlApplyResult
            {
                Control        = V2CameraControl.OpticalStabilization,
                ReadbackStatus = V2ControlReadbackStatus.Unsupported,
                WarningMessage = "OpticalImageStabilizationControl not supported on this device.",
            });

        bool applied = false;
        string? error = null;
        try
        {
            ois.Mode = enabled
                ? OpticalImageStabilizationMode.On
                : OpticalImageStabilizationMode.Off;
            applied = true;
        }
        catch (Exception ex) { error = ex.Message; }

        return Task.FromResult(new V2ControlApplyResult
        {
            Control        = V2CameraControl.OpticalStabilization,
            Applied        = applied,
            ReadbackValue  = ois.Mode == OpticalImageStabilizationMode.Off ? 0 : 1,
            ReadbackStatus = applied ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Failed,
            WarningMessage = error,
        });
    }

    /// <summary>
    /// Attempts to set flicker reduction to Auto mode so the driver self-detects 50/60 Hz from
    /// artificial lighting. Always returns Unsupported today — see remarks.
    /// </summary>
    /// <remarks>
    /// <c>VideoDeviceController.FlickerReductionControl</c> is not present in the public WinRT
    /// metadata (<c>Windows.winmd</c>) of ANY installed Windows SDK, including the newest one
    /// (verified 10.0.26100.0) — this isn't a "our target SDK is too old" problem. A prior version
    /// of this method used C# <c>dynamic</c> dispatch to try to reach the member anyway, on the
    /// theory that the runtime COM object might expose it even if the compile-time projection
    /// didn't. That doesn't work for CsWinRT-projected WinRT objects: unlike classic COM
    /// <c>IDispatch</c> automation objects, they don't implement <see cref="System.Dynamic.IDynamicMetaObjectProvider"/>,
    /// so the C# dynamic binder falls back to reflecting over the *compile-time* type — which is
    /// the same lookup a direct (non-dynamic) call would do, just deferred to a guaranteed-to-throw
    /// <c>RuntimeBinderException</c> at runtime on every single call instead of a compile error.
    /// Reaching this control for real would require raw COM interop against the underlying
    /// DirectShow/Media Foundation camera-control interfaces (e.g. <c>IAMCameraControl</c>/
    /// <c>IKsControl</c>), bypassing the WinRT projection entirely — not attempted here.
    /// Short-circuiting to Unsupported avoids a wasted exception per camera per session while
    /// producing the exact same practical outcome (flicker reduction is never actually applied).
    /// </remarks>
    public Task<V2ControlApplyResult> SetFlickerReductionAutoAsync(CancellationToken ct = default)
    {
        if (_controller is null) return Task.FromResult(NotAttached(V2CameraControl.FlickerReduction));

        return Task.FromResult(new V2ControlApplyResult
        {
            Control        = V2CameraControl.FlickerReduction,
            Applied        = false,
            ReadbackStatus = V2ControlReadbackStatus.Unsupported,
            WarningMessage = "Flicker reduction is not exposed by the WinRT camera control API on " +
                "this system (VideoDeviceController.FlickerReductionControl is absent from all " +
                "installed Windows SDK metadata) and cannot be applied by MultiCamApp.",
        });
    }

    // Kept as a no-op fallback when EnableFlickerReduction is false (reports current probe state).
    public V2ControlApplyResult ProbeFlickerReduction() => new()
    {
        Control        = V2CameraControl.FlickerReduction,
        Applied        = false,
        ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
        WarningMessage = "Flicker reduction disabled in VideoEngineSettings.",
    };

    /// <summary>
    /// Sets white balance to Auto preset so the camera behaviour matches Windows Camera.
    /// Gracefully returns Unsupported when the driver does not expose WhiteBalanceControl.
    /// </summary>
    public async Task<V2ControlApplyResult> SetWhiteBalanceAutoAsync(CancellationToken ct = default)
    {
        if (_controller is null) return NotAttached(V2CameraControl.WhiteBalance);

        var wbCtrl = _controller.WhiteBalanceControl;
        if (!wbCtrl.Supported)
            return new V2ControlApplyResult
            {
                Control        = V2CameraControl.WhiteBalance,
                ReadbackStatus = V2ControlReadbackStatus.Unsupported,
                WarningMessage = "WhiteBalanceControl not supported on this device.",
            };

        bool applied = false;
        string? error = null;
        try
        {
            await wbCtrl.SetPresetAsync(ColorTemperaturePreset.Auto).AsTask(ct);
            applied = true;
        }
        catch (Exception ex) { error = ex.Message; }

        return new V2ControlApplyResult
        {
            Control        = V2CameraControl.WhiteBalance,
            Applied        = applied,
            ReadbackStatus = applied ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Failed,
            WarningMessage = error,
        };
    }

    // ── Windows Studio Effects ─────────────────────────────────────────────────

    private void CheckWindowsStudioEffects()
    {
        if (!VideoEngineSettings.WarnOnWindowsStudioEffects) { WindowsStudioEffectsWarning = null; return; }

        var version = Environment.OSVersion.Version;
        WindowsStudioEffectsWarning = version.Major >= 10 && version.Build >= 22621
            ? "WARNING: Windows 11 22H2+ detected. Studio Effects (eye contact, background blur, " +
              "auto framing) may be active on integrated cameras. " +
              "Disable in Windows Settings → Bluetooth & devices → Cameras before recording."
            : null;
    }

    // ── Environmental lock (calibration state machine) ────────────────────────

    /// <summary>
    /// "Freeze and Read" calibration lock. Disables all auto modes (focus, exposure, WB, ISO),
    /// reads the hardware-frozen values, and returns them so the UI can populate sliders.
    /// Must be called while preview is active — auto values are only accessible after ISP stabilises.
    /// </summary>
    public async Task<V2EnvironmentLockResult> ExecuteEnvironmentalLockAsync(CancellationToken ct = default)
    {
        if (_controller is null)
            return new V2EnvironmentLockResult { Warning = "Camera not attached." };

        bool focusLocked = false;
        uint focusAt = 0;
        bool exposureLocked = false;
        double exposureAtS = 0;
        bool wbLocked = false;
        uint wbAtK = 0;
        var warnings = new List<string>();

        // 1. Freeze Focus — switch to Manual so the driver writes its current step into the registers.
        var fc = _controller.FocusControl;
        if (fc.Supported)
        {
            try
            {
                var modes = fc.SupportedFocusModes;
                if (fc.Mode != FocusMode.Manual && modes.Contains(FocusMode.Manual))
                    fc.Configure(new FocusSettings { Mode = FocusMode.Manual, DisableDriverFallback = true });
                focusAt     = fc.Value;
                focusLocked = true;
            }
            catch (Exception ex) { warnings.Add($"Focus: {ex.Message}"); }
        }

        // 2. Freeze Exposure — SetAutoAsync(false) freezes shutter registers; Value is then readable.
        var ec = _controller.ExposureControl;
        if (ec.Supported)
        {
            try
            {
                if (ec.Auto)
                    await ec.SetAutoAsync(false).AsTask(ct);
                exposureAtS    = ec.Value.TotalSeconds;
                exposureLocked = true;
            }
            catch (Exception ex) { warnings.Add($"Exposure: {ex.Message}"); }
        }

        // 3. Freeze White Balance — switch preset to Manual so Value reflects the frozen Kelvin temp.
        var wbc = _controller.WhiteBalanceControl;
        if (wbc.Supported)
        {
            try
            {
                if (wbc.Preset == ColorTemperaturePreset.Auto)
                    await wbc.SetPresetAsync(ColorTemperaturePreset.Manual).AsTask(ct);
                wbAtK    = wbc.Value;
                wbLocked = true;
            }
            catch (Exception ex) { warnings.Add($"WB: {ex.Message}"); }
        }

        // 4. ISO/Gain lock — prevents driver from silently spiking gain to compensate for shadowing.
        bool isoLocked = await EnforceIsoLockAsync(ct);

        AppDiagnosticLogger.Runtime(
            $"ENV_LOCK focus={focusLocked}@{focusAt} exp={exposureLocked}@{exposureAtS * 1000:F1}ms " +
            $"wb={wbLocked}@{wbAtK}K iso={isoLocked}" +
            (warnings.Count > 0 ? $" warn=[{string.Join("; ", warnings)}]" : ""));

        return new V2EnvironmentLockResult
        {
            FocusLocked            = focusLocked,
            FocusLockedAt          = focusAt,
            ExposureLocked         = exposureLocked,
            ExposureLockedAtS      = exposureAtS,
            WhiteBalanceLocked     = wbLocked,
            WhiteBalanceLockedAtK  = wbAtK,
            IsoLocked              = isoLocked,
            Warning                = warnings.Count > 0 ? string.Join("; ", warnings) : null,
        };
    }

    /// <summary>
    /// Locks ISO/Analog Gain to its current hardware value.
    /// Without this, cheap webcam drivers silently spike gain when subjects move into shadows,
    /// injecting digital noise that breaks pixel-threshold tracking (e.g. DeepLabCut).
    /// </summary>
    private async Task<bool> EnforceIsoLockAsync(CancellationToken ct = default)
    {
        var iso = _controller?.IsoSpeedControl;
        if (iso is null || !iso.Supported) return false;
        try
        {
            if (iso.Auto)
            {
                var current = iso.Value;
                await iso.SetValueAsync(current).AsTask(ct);
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Sets white balance to an explicit Kelvin value via <see cref="WhiteBalanceControl.SetValueAsync"/>.
    /// Called by the WB slider after environmental lock is active.
    /// </summary>
    public async Task<V2ControlApplyResult> SetWhiteBalanceManualAsync(
        uint kelvins, CancellationToken ct = default)
    {
        if (_controller is null) return NotAttached(V2CameraControl.WhiteBalance);

        var wbc = _controller.WhiteBalanceControl;
        if (!wbc.Supported)
            return new V2ControlApplyResult
            {
                Control        = V2CameraControl.WhiteBalance,
                ReadbackStatus = V2ControlReadbackStatus.Unsupported,
            };

        bool applied = false;
        string? error = null;
        try
        {
            if (wbc.Preset != ColorTemperaturePreset.Manual)
                await wbc.SetPresetAsync(ColorTemperaturePreset.Manual).AsTask(ct);
            await wbc.SetValueAsync(kelvins).AsTask(ct);
            applied = true;
        }
        catch (Exception ex) { error = ex.Message; }

        uint readback = 0;
        try { readback = wbc.Value; } catch { }

        return new V2ControlApplyResult
        {
            Control        = V2CameraControl.WhiteBalance,
            Applied        = applied,
            ReadbackValue  = readback,
            ReadbackStatus = applied ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Failed,
            WarningMessage = error,
        };
    }

    /// <summary>
    /// One-Shot Auto Calibrate: enables auto modes, waits for ISP convergence (2.5 s),
    /// then freezes the settled values via <see cref="ExecuteEnvironmentalLockAsync"/>.
    /// Gives researchers a reliable baseline without manual slider guessing.
    /// </summary>
    public async Task<V2EnvironmentLockResult> OneShotCalibrateAsync(CancellationToken ct = default)
    {
        if (_controller is null)
            return new V2EnvironmentLockResult { Warning = "Camera not attached." };

        // Enable auto modes so the ISP can converge to the current lighting conditions.
        try { if (_controller.ExposureControl.Supported)     await _controller.ExposureControl.SetAutoAsync(true).AsTask(ct); }     catch { }
        try { if (_controller.WhiteBalanceControl.Supported) await _controller.WhiteBalanceControl.SetPresetAsync(ColorTemperaturePreset.Auto).AsTask(ct); } catch { }

        // 2.5 s for auto-exposure and auto-white-balance ISP loops to reach convergence.
        await Task.Delay(2500, ct);

        return await ExecuteEnvironmentalLockAsync(ct);
    }

    /// <summary>
    /// Releases the environmental lock by restoring all auto modes: exposure, white balance, and ISO.
    /// Call when the researcher needs to return to a pre-calibration state.
    /// ISO auto is restored explicitly because <see cref="EnforceIsoLockAsync"/> pinned gain;
    /// without this call cheap webcam drivers stay pinned and may under-expose on next open.
    /// </summary>
    public async Task ReleaseEnvironmentalLockAsync(CancellationToken ct = default)
    {
        if (_controller is null) return;

        try { if (_controller.ExposureControl.Supported)     await _controller.ExposureControl.SetAutoAsync(true).AsTask(ct); }     catch { }
        try { if (_controller.WhiteBalanceControl.Supported) await _controller.WhiteBalanceControl.SetPresetAsync(ColorTemperaturePreset.Auto).AsTask(ct); } catch { }
        try
        {
            // Reset ISO gain to adaptive tracking so the hardware loop can re-scale freely.
            if (_controller.IsoSpeedControl.Supported)
                await _controller.IsoSpeedControl.SetAutoAsync().AsTask(ct);
        }
        catch { }

        AppDiagnosticLogger.Runtime("ENV_LOCK_RELEASE: auto exposure, white balance and ISO gain restored.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static V2ControlApplyResult NotAttached(V2CameraControl control) => new()
    {
        Control        = control,
        Applied        = false,
        ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
        WarningMessage = "CameraControlManagerV2 not attached to an open camera session.",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed   = true;
        _controller = null;
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

public sealed class V2ExposureRequest
{
    public bool AutoExposure { get; init; } = false;
    /// <summary>Manual shutter duration in seconds. Ignored when AutoExposure is true.</summary>
    public double? ManualValue { get; init; }
}

public sealed class V2FocusRequest
{
    public bool AutoFocus { get; init; } = false;
    public double? ManualValue { get; init; }
}

public sealed class V2ControlApplyResult
{
    public V2CameraControl Control { get; init; }
    public bool Applied { get; init; }
    public double? ReadbackValue { get; init; }
    public V2ControlReadbackStatus ReadbackStatus { get; init; }
    public string? WarningMessage { get; init; }

    internal static V2ControlApplyResult Skeleton(V2CameraControl control) => new()
    {
        Control        = control,
        Applied        = false,
        ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
        WarningMessage = "Control not applied in this build.",
    };
}

public enum V2CameraControl
{
    Exposure,
    Focus,
    WhiteBalance,
    LowLightCompensation,
    OpticalStabilization,
    FlickerReduction,
}

public enum V2ControlReadbackStatus
{
    NotAttempted,
    Confirmed,
    Mismatch,
    Unsupported,
    Failed,
}

/// <summary>
/// Snapshot of driver-reported capability ranges probed from an open camera session.
/// Exposure values are in seconds (TimeSpan.TotalSeconds). Focus values are in driver-dependent steps.
/// White balance values are in Kelvin (uint). ISO values are in driver-native units.
/// </summary>
public sealed class V2CameraCapabilitySnapshot
{
    public bool   NotAttached   { get; init; }
    // Exposure (seconds)
    public bool   ExposureSupported  { get; init; }
    public double ExposureMinS       { get; init; }
    public double ExposureMaxS       { get; init; }
    public double ExposureStepS      { get; init; }
    public double ExposureCurrentS   { get; init; }
    // Focus (driver steps)
    public bool   FocusSupported     { get; init; }
    public uint   FocusMin           { get; init; }
    public uint   FocusMax           { get; init; }
    public uint   FocusStep          { get; init; }
    public uint   FocusCurrent       { get; init; }
    // Low-light
    public bool   LowLightCompensationSupported { get; init; }
    // White balance (Kelvin)
    public bool   WhiteBalanceSupported   { get; init; }
    public uint   WhiteBalanceMinK        { get; init; }
    public uint   WhiteBalanceMaxK        { get; init; }
    public uint   WhiteBalanceStepK       { get; init; }
    public uint   WhiteBalanceCurrentK    { get; init; }
    public bool   WhiteBalanceInAutoMode  { get; init; }
    // ISO / Analog Gain
    public bool   IsoSupported  { get; init; }
    public uint   IsoMin        { get; init; }
    public uint   IsoMax        { get; init; }
    public uint   IsoStep       { get; init; }
    public uint   IsoCurrent    { get; init; }
    public bool   IsoIsAuto     { get; init; }
}

/// <summary>
/// Result of <see cref="CameraControlManagerV2.ExecuteEnvironmentalLockAsync"/> or
/// <see cref="CameraControlManagerV2.OneShotCalibrateAsync"/>. Contains the frozen hardware
/// values so the UI can populate sliders immediately after locking.
/// </summary>
public sealed class V2EnvironmentLockResult
{
    /// <summary>True if focus was successfully switched to manual and a position value was read back.</summary>
    public bool   FocusLocked            { get; init; }
    /// <summary>Frozen focus position in driver steps.</summary>
    public uint   FocusLockedAt          { get; init; }
    /// <summary>True if exposure auto was disabled and the shutter duration was read back.</summary>
    public bool   ExposureLocked         { get; init; }
    /// <summary>Frozen shutter duration in seconds.</summary>
    public double ExposureLockedAtS      { get; init; }
    /// <summary>True if white balance was switched to manual and a Kelvin value was read back.</summary>
    public bool   WhiteBalanceLocked     { get; init; }
    /// <summary>Frozen colour temperature in Kelvin.</summary>
    public uint   WhiteBalanceLockedAtK  { get; init; }
    /// <summary>True if ISO/gain was clamped to its current value.</summary>
    public bool   IsoLocked              { get; init; }
    /// <summary>Non-null if any control produced a non-fatal warning during locking.</summary>
    public string? Warning               { get; init; }
}
