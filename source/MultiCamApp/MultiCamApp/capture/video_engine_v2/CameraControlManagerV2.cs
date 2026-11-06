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

    // True only while ApplyResearchDefaults runs at camera open (before frames flow): UVC-only
    // controls are then reported, not locked, so auto exposure/focus can settle during preview.
    private bool _atCameraOpen;

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

    public void Detach()
    {
        if (_uvcExposureChangedByUser)
        {
            try { UvcExposure?.TrySetAuto(true); } catch { /* best effort: camera may be gone */ }
            _uvcExposureChangedByUser = false;
        }
        if (_uvcFocusChangedByUser)
        {
            try
            {
                if (UvcFocus is { } f)
                {
                    if (_uvcFocusOriginalValue is { } o) { f.TrySetAuto(false); f.TrySetValue(o); }
                    f.TrySetAuto(true);
                }
            }
            catch { /* best effort */ }
            _uvcFocusChangedByUser = false;
            _uvcFocusOriginalValue = null;
        }
        _controller = null;
    }

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

        _atCameraOpen = true;
        try
        {
            if (VideoEngineSettings.DisableAutoFocus)
                results.Add(await DisableFocusAsync(ct).ConfigureAwait(false));

            if (VideoEngineSettings.DisableAutoExposure)
                results.Add(await DisableAutoExposureAsync(ct).ConfigureAwait(false));
        }
        finally { _atCameraOpen = false; }

        // Only disable low-light compensation when exposure is actually controllable
        // (ExposureControl.Supported). When exposure can't be touched by the app at all,
        // the camera's onboard auto-exposure runs uncontrolled regardless — disabling LLC
        // in that case only darkens the picture without buying any recording reproducibility.
        if (_controller.ExposureControl.Supported)
        {
            results.Add(await SetLowLightCompensationAsync(enabled: false, ct).ConfigureAwait(false));
        }
        else if (UvcExposure is not null)
        {
            results.Add(new V2ControlApplyResult
            {
                Control        = V2CameraControl.LowLightCompensation,
                Applied        = false,
                ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
                // Must match what LockUvcControlsForRecording actually does (the exposure lock is opt-in).
                WarningMessage = VideoEngineSettings.DisableAutoExposure && VideoEngineSettings.LockUvcExposureDuringRecording
                    ? "Not changed — exposure is locked via the UVC exposure control when recording starts, so backlight/low-light compensation cannot alter brightness during the recording."
                    : "Not changed — this camera has no separate low-light compensation control (UVC exposure only). Whether exposure was auto or manual during the recording is stated in the exposure lines.",
            });
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
        if (!fc.Supported && UvcFocus is { } uvcFocus)
            return _atCameraOpen
                ? UvcPreviewStatus(V2CameraControl.Focus, uvcFocus, v => v)
                : LockUvc(V2CameraControl.Focus, uvcFocus, v => v);
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
        if (!ec.Supported && UvcExposure is { } uvcExp)
        {
            if (_atCameraOpen && !request.AutoExposure && !request.ManualValue.HasValue)
                return UvcPreviewStatus(V2CameraControl.Exposure, uvcExp, v => UvcExposureToSeconds(v));
            try
            {
                var okAuto = uvcExp.TrySetAuto(request.AutoExposure);
                var okValue = request.AutoExposure || !request.ManualValue.HasValue
                    || uvcExp.TrySetValue(Math.Clamp(SecondsToUvcExposure(request.ManualValue.Value), uvcExp.Capabilities.Min, uvcExp.Capabilities.Max));
                uvcExp.TryGetAuto(out var nowAuto);
                double? sec = uvcExp.TryGetValue(out var raw) ? UvcExposureToSeconds(raw) : null;
                return new V2ControlApplyResult
                {
                    Control        = V2CameraControl.Exposure,
                    Applied        = okAuto && okValue,
                    ReadbackValue  = sec,
                    AutoModeActive = nowAuto,
                    ReadbackStatus = okAuto && okValue && nowAuto == request.AutoExposure
                        ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Mismatch,
                };
            }
            catch (Exception ex)
            {
                return new V2ControlApplyResult { Control = V2CameraControl.Exposure, ReadbackStatus = V2ControlReadbackStatus.Failed, WarningMessage = ex.Message };
            }
        }
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

    // Set when the user switched UVC exposure to manual; the camera keeps UVC settings after the
    // app exits, so Detach() restores auto exposure (other apps / the next session start clean).
    private bool _uvcExposureChangedByUser;

    /// <summary>
    /// STABLE_CORE_V2 exception #13: the Advanced Camera Controls exposure slider for VideoEngineV2.
    /// Previously the UI sent the request to the dormant legacy pipeline, which opened a
    /// SharedReadOnly MediaCapture that cannot change controls, and reported "Not supported".
    /// <paramref name="sliderIndex"/> (0–255) maps linearly onto the camera's own range
    /// (UVC: log2-second steps; ExposureControl: duration).
    /// </summary>
    public async Task<V2ControlApplyResult> ApplyUserExposureAsync(
        bool autoExposure, double? sliderIndex, CancellationToken ct = default)
    {
        if (_controller is null) return NotAttached(V2CameraControl.Exposure);
        double? seconds = null;
        if (!autoExposure && sliderIndex is { } idx)
        {
            var t = Math.Clamp(idx, 0, 255) / 255.0;
            var ec = _controller.ExposureControl;
            if (ec.Supported)
                seconds = ec.Min.TotalSeconds + t * (ec.Max - ec.Min).TotalSeconds;
            else if (UvcExposure is { } u)
                seconds = UvcExposureToSeconds(SliderToUvcExposureLog2(idx, u.Capabilities.Min, u.Capabilities.Max));
        }
        var result = await SetExposureAsync(
            new V2ExposureRequest { AutoExposure = autoExposure, ManualValue = seconds }, ct).ConfigureAwait(false);
        if (UvcExposure is not null)
            _uvcExposureChangedByUser = !autoExposure && result.Applied;
        AppDiagnosticLogger.Runtime($"V2_USER_EXPOSURE auto={autoExposure} slider={sliderIndex} requestS={seconds:F6} " +
            $"applied={result.Applied} readback={result.ReadbackStatus} valueS={result.ReadbackValue:F6}");
        return result;
    }

    private bool _uvcFocusChangedByUser;
    // Lens position before the first user change. Restoring only the auto flag is not enough: on a C922 the
    // autofocus did not search again (position stayed at 250 for 6 s after re-enabling, verified by probe),
    // so the next session started defocused and its recording-start focus lock froze that blur.
    private double? _uvcFocusOriginalValue;

    /// <summary>
    /// 2.1.0 (exception #13, focus part): the Advanced Camera Controls focus Apply button for VideoEngineV2.
    /// Same defect as exposure — the request went to the dormant legacy pipeline (SharedReadOnly) and never
    /// reached the camera. <paramref name="value"/> is in the camera's own focus units (the slider range is
    /// taken from <see cref="ProbeCapabilities"/>).
    /// </summary>
    public async Task<V2ControlApplyResult> ApplyUserFocusAsync(bool autoFocus, double? value, CancellationToken ct = default)
    {
        if (_controller is null) return NotAttached(V2CameraControl.Focus);
        try
        {
            var fc = _controller.FocusControl;
            if (fc.Supported)
            {
                if (autoFocus)
                {
                    var mode = fc.SupportedFocusModes.Contains(FocusMode.Continuous) ? FocusMode.Continuous : FocusMode.Auto;
                    fc.Configure(new FocusSettings { Mode = mode });
                    await fc.FocusAsync().AsTask(ct);
                }
                else
                {
                    fc.Configure(new FocusSettings { Mode = fc.SupportedFocusModes.Contains(FocusMode.Manual) ? FocusMode.Manual : FocusMode.Single, DisableDriverFallback = true });
                    if (value is { } v)
                        await fc.SetValueAsync((uint)Math.Clamp(Math.Round(v), fc.Min, fc.Max)).AsTask(ct);
                }
                return new V2ControlApplyResult
                {
                    Control = V2CameraControl.Focus, Applied = true, ReadbackValue = fc.Value,
                    ReadbackStatus = V2ControlReadbackStatus.Confirmed, AutoModeActive = autoFocus,
                };
            }
            if (UvcFocus is { } u)
            {
                if (_uvcFocusOriginalValue is null && u.TryGetValue(out var orig)) _uvcFocusOriginalValue = orig;
                var okAuto = u.TrySetAuto(autoFocus);
                var okValue = autoFocus || value is null || u.TrySetValue(SnapToStep(value.Value, u.Capabilities.Min, u.Capabilities.Max, u.Capabilities.Step));
                u.TryGetAuto(out var nowAuto);
                double? rb = u.TryGetValue(out var raw) ? raw : null;
                _uvcFocusChangedByUser = !autoFocus && okAuto && okValue;
                AppDiagnosticLogger.Runtime($"V2_USER_FOCUS auto={autoFocus} request={value} applied={okAuto && okValue} nowAuto={nowAuto} readback={rb}");
                return new V2ControlApplyResult
                {
                    Control = V2CameraControl.Focus, Applied = okAuto && okValue, ReadbackValue = rb, AutoModeActive = nowAuto,
                    ReadbackStatus = okAuto && okValue && nowAuto == autoFocus ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Mismatch,
                };
            }
            return new V2ControlApplyResult { Control = V2CameraControl.Focus, ReadbackStatus = V2ControlReadbackStatus.Unsupported };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new V2ControlApplyResult { Control = V2CameraControl.Focus, ReadbackStatus = V2ControlReadbackStatus.Failed, WarningMessage = ex.Message };
        }
    }

    /// <summary>Rounds a value onto a control's step grid inside [min, max] (UVC controls reject off-grid values).</summary>
    public static double SnapToStep(double value, double min, double max, double step)
    {
        var v = Math.Clamp(value, min, max);
        return step > 0 ? Math.Clamp(min + Math.Round((v - min) / step) * step, min, max) : Math.Round(v);
    }

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
        else if (UvcExposure is { } uvcExp)
        {
            // STABLE_CORE_V2 exception #13: UVC-only webcams (C922, JVU250, laptop cameras) were
            // shown "Exposure: Not supported" although their UVC exposure control works.
            try
            {
                expSupported = true;
                expMin       = UvcExposureToSeconds(uvcExp.Capabilities.Min);
                expMax       = UvcExposureToSeconds(uvcExp.Capabilities.Max);
                expCurrent   = uvcExp.TryGetValue(out var ev) ? UvcExposureToSeconds(ev) : 0;
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
        else if (UvcFocus is { } uvcFoc)
        {
            try
            {
                focSupported = true;
                focMin       = (uint)Math.Max(0, uvcFoc.Capabilities.Min);
                focMax       = (uint)Math.Max(0, uvcFoc.Capabilities.Max);
                focStep      = (uint)Math.Max(0, uvcFoc.Capabilities.Step);
                focCurrent   = uvcFoc.TryGetValue(out var fv) ? (uint)Math.Max(0, fv) : 0;
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

        // STABLE_CORE_V2 exception #9: the remarks above are about FlickerReductionControl only.
        // VideoDeviceController.TryGet/TrySetPowerlineFrequency (Windows.Media.Devices, UVC
        // "Power Line Frequency") IS available and was verified on real UVC webcams (read 60 Hz,
        // set 50 Hz, read back 50 Hz). Previously this method always reported "Unsupported".
        // ReadbackValue: 50 / 60 = Hz, 0 = anti-flicker disabled, -1 = camera auto.
        try
        {
            if (!_controller.TryGetPowerlineFrequency(out var current))
                return Task.FromResult(new V2ControlApplyResult
                {
                    Control        = V2CameraControl.FlickerReduction,
                    ReadbackStatus = V2ControlReadbackStatus.Unsupported,
                    WarningMessage = "Power line frequency (anti-flicker) control not supported on this device.",
                });

            var requested = VideoEngineSettings.PowerlineFrequencyHz switch
            {
                50 => (PowerlineFrequency?)PowerlineFrequency.FiftyHertz,
                60 => PowerlineFrequency.SixtyHertz,
                -1 => PowerlineFrequency.Auto,
                _  => null, // 0 = keep the camera's own setting
            };
            var applied = false;
            if (requested is { } r && r != current)
                applied = _controller.TrySetPowerlineFrequency(r);
            _controller.TryGetPowerlineFrequency(out var readback);
            // 2.1.0: after a request for Auto, cameras commonly read back the frequency they detected
            // (50/60) — that is a successful Auto, not a mismatch. A camera that refuses Auto keeps its
            // previous value, which can produce moving bands if it does not match the mains.
            var autoResolved = requested == PowerlineFrequency.Auto
                && (applied || current == PowerlineFrequency.Auto)
                && readback is PowerlineFrequency.Auto or PowerlineFrequency.FiftyHertz or PowerlineFrequency.SixtyHertz;
            var autoRefused = requested == PowerlineFrequency.Auto && !autoResolved;
            var status = requested is null || readback == requested || autoResolved
                ? V2ControlReadbackStatus.Confirmed
                : V2ControlReadbackStatus.Mismatch;
            return Task.FromResult(new V2ControlApplyResult
            {
                Control        = V2CameraControl.FlickerReduction,
                Applied        = applied || (requested is not null && readback == requested) || autoResolved,
                ReadbackValue  = PowerlineToNumber(readback),
                ReadbackStatus = status,
                WarningMessage = requested is null
                    ? $"Camera setting kept: {PowerlineToNumber(readback)} (50/60 = Hz, 0 = off, -1 = auto). Set powerLineFrequencyHz to 50 or 60 to match local mains."
                    : autoRefused
                        ? $"This camera does not accept automatic anti-flicker; it stays at {PowerlineToNumber(readback)} (50/60 = Hz, 0 = off). If moving horizontal bands appear, choose 50 or 60 Hz to match the local mains."
                        : autoResolved && readback != PowerlineFrequency.Auto
                            ? $"Auto anti-flicker: camera selected {PowerlineToNumber(readback)} Hz."
                            : null,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new V2ControlApplyResult
            {
                Control        = V2CameraControl.FlickerReduction,
                ReadbackStatus = V2ControlReadbackStatus.Failed,
                WarningMessage = ex.Message,
            });
        }
    }

    private static double PowerlineToNumber(PowerlineFrequency f) => f switch
    {
        PowerlineFrequency.FiftyHertz => 50,
        PowerlineFrequency.SixtyHertz => 60,
        PowerlineFrequency.Auto       => -1,
        _                             => 0,
    };

    // ── STABLE_CORE_V2 exception #9: standard UVC control fallback ────────────────────────────
    // Most USB webcams expose exposure / focus / white balance only through the classic
    // VideoDeviceController.Exposure/Focus/WhiteBalance (MediaDeviceControl, UVC camera-terminal
    // controls) and NOT through the newer ExposureControl/FocusControl/WhiteBalanceControl. The
    // app used only the newer APIs, so these cameras were reported "Not supported" and auto
    // exposure/focus stayed active during recordings. Verified on hardware: JVU250 and a laptop
    // webcam support Exposure (log2 seconds), WhiteBalance (Kelvin) and, for JVU250, Focus.

    private MediaDeviceControl? UvcExposure =>
        _controller?.Exposure is { } c && c.Capabilities.Supported && !_controller.ExposureControl.Supported ? c : null;
    private MediaDeviceControl? UvcFocus =>
        _controller?.Focus is { } c && c.Capabilities.Supported && !_controller.FocusControl.Supported ? c : null;
    private MediaDeviceControl? UvcWhiteBalance =>
        _controller?.WhiteBalance is { } c && c.Capabilities.Supported && !_controller.WhiteBalanceControl.Supported ? c : null;

    /// <summary>UVC/DirectShow exposure values are log2(seconds): -6 → 1/64 s.</summary>
    public static double UvcExposureToSeconds(double log2Seconds) => Math.Pow(2, log2Seconds);
    public static double SecondsToUvcExposure(double seconds) => Math.Round(Math.Log2(Math.Max(seconds, 1e-6)));
    /// <summary>Maps the UI exposure slider (0–255) linearly onto a UVC log2-second range, rounded to a whole step.</summary>
    public static double SliderToUvcExposureLog2(double sliderIndex, double uvcMin, double uvcMax) =>
        Math.Round(uvcMin + Math.Clamp(sliderIndex, 0, 255) / 255.0 * (uvcMax - uvcMin));

    private readonly HashSet<V2CameraControl> _uvcLockedForRecording = new();

    /// <summary>
    /// Locks UVC-only exposure / focus / white balance at their current (converged) values when
    /// recording starts, per <see cref="VideoEngineSettings.DisableAutoExposure"/>/
    /// <see cref="VideoEngineSettings.DisableAutoFocus"/>, and records readbacks in
    /// <see cref="LastAppliedResults"/> so the recording metadata reports the real state.
    /// Cameras with the newer controls are unaffected (already handled at open).
    /// </summary>
    public IReadOnlyList<V2ControlApplyResult> LockUvcControlsForRecording()
    {
        var results = new List<V2ControlApplyResult>();
        if (_controller is null) return results;
        if (VideoEngineSettings.DisableAutoExposure && VideoEngineSettings.LockUvcExposureDuringRecording && UvcExposure is { } exp)
            results.Add(LockUvc(V2CameraControl.Exposure, exp, v => UvcExposureToSeconds(v)));
        else if (UvcExposure is { } expManual && expManual.Capabilities.AutoModeSupported
                 && expManual.TryGetAuto(out var expIsAuto) && !expIsAuto)
            results.Add(new V2ControlApplyResult
            {
                Control        = V2CameraControl.Exposure,
                Applied        = true,
                ReadbackValue  = expManual.TryGetValue(out var mv) ? UvcExposureToSeconds(mv) : null,
                ReadbackStatus = V2ControlReadbackStatus.Confirmed,
                AutoModeActive = false,
                WarningMessage = "Manual exposure (set in Advanced Camera Controls) kept for the recording (UVC control).",
            });
        else if (UvcExposure is { } expAuto)
            results.Add(new V2ControlApplyResult
            {
                Control        = V2CameraControl.Exposure,
                Applied        = false,
                ReadbackValue  = expAuto.TryGetValue(out var ev) ? UvcExposureToSeconds(ev) : null,
                ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
                AutoModeActive = expAuto.Capabilities.AutoModeSupported && expAuto.TryGetAuto(out var ea) ? ea : null,
                WarningMessage = "Camera auto exposure kept (UVC). Anti-flicker stays effective; set exposure manually in Advanced Camera Controls if a fixed shutter is required.",
            });
        if (VideoEngineSettings.DisableAutoFocus && UvcFocus is { } foc)
            results.Add(LockUvc(V2CameraControl.Focus, foc, v => v));
        if (VideoEngineSettings.LockWhiteBalanceDuringRecording && UvcWhiteBalance is { } wb)
            results.Add(LockUvc(V2CameraControl.WhiteBalance, wb, v => v));
        if (results.Count > 0)
        {
            var merged = LastAppliedResults.Where(r => results.All(n => n.Control != r.Control)).ToList();
            merged.AddRange(results);
            LastAppliedResults = merged.AsReadOnly();
            foreach (var r in results)
                AppDiagnosticLogger.Runtime($"V2_CONTROL_UVC_LOCK control={r.Control} applied={r.Applied} readback={r.ReadbackStatus} value={r.ReadbackValue:F6}" +
                    (r.WarningMessage is not null ? $" warn={r.WarningMessage}" : ""));
        }
        return results;
    }

    private V2ControlApplyResult LockUvc(V2CameraControl control, MediaDeviceControl c, Func<double, double> toReported)
    {
        try
        {
            var autoSupported = c.Capabilities.AutoModeSupported;
            var wasAuto = autoSupported && c.TryGetAuto(out var a) && a;
            var ok = !autoSupported || c.TrySetAuto(false);
            var nowAuto = autoSupported && c.TryGetAuto(out var a2) && a2;
            double? value = c.TryGetValue(out var v) ? toReported(v) : null;
            if (ok && wasAuto) _uvcLockedForRecording.Add(control);
            return new V2ControlApplyResult
            {
                Control        = control,
                Applied        = ok && !nowAuto,
                ReadbackValue  = value,
                AutoModeActive = autoSupported ? nowAuto : null,
                ReadbackStatus = ok && !nowAuto ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Mismatch,
                WarningMessage = ok && !nowAuto ? "Locked at the value settled during preview (UVC control)." : "Auto mode could not be disabled (UVC control).",
            };
        }
        catch (Exception ex)
        {
            return new V2ControlApplyResult { Control = control, ReadbackStatus = V2ControlReadbackStatus.Failed, WarningMessage = ex.Message };
        }
    }

    /// <summary>Restores auto mode for every UVC control locked by <see cref="LockUvcControlsForRecording"/>.</summary>
    public void ReleaseUvcRecordingLocks()
    {
        if (_controller is null) { _uvcLockedForRecording.Clear(); return; }
        foreach (var control in _uvcLockedForRecording)
        {
            try
            {
                var c = control switch
                {
                    V2CameraControl.Exposure     => _controller.Exposure,
                    V2CameraControl.Focus        => _controller.Focus,
                    V2CameraControl.WhiteBalance => _controller.WhiteBalance,
                    _ => null,
                };
                c?.TrySetAuto(true);
            }
            catch { /* best effort */ }
        }
        if (_uvcLockedForRecording.Count > 0)
            AppDiagnosticLogger.Runtime($"V2_CONTROL_UVC_RELEASE controls={string.Join(",", _uvcLockedForRecording)}");
        _uvcLockedForRecording.Clear();
    }

    private V2ControlApplyResult UvcPreviewStatus(V2CameraControl control, MediaDeviceControl c, Func<double, double> toReported) => new()
    {
        Control        = control,
        Applied        = false,
        ReadbackValue  = c.TryGetValue(out var v) ? toReported(v) : null,
        ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
        WarningMessage = control == V2CameraControl.Focus
            ? "Supported via standard UVC control: autofocus runs during preview and is locked when recording starts."
            : "Supported via standard UVC control: camera auto mode kept (manual control available in Advanced Camera Controls).",
    };

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
        if (!wbCtrl.Supported && UvcWhiteBalance is { } uvcWb)
        {
            var ok = !uvcWb.Capabilities.AutoModeSupported || uvcWb.TrySetAuto(true);
            return new V2ControlApplyResult
            {
                Control        = V2CameraControl.WhiteBalance,
                Applied        = ok,
                ReadbackValue  = uvcWb.TryGetValue(out var k) ? k : null,
                ReadbackStatus = ok ? V2ControlReadbackStatus.Confirmed : V2ControlReadbackStatus.Failed,
                WarningMessage = "Auto white balance (standard UVC control).",
            };
        }
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

        // STABLE_CORE_V2 exception #9: same lock for cameras that only expose UVC controls.
        if (!focusLocked && UvcFocus is { } uf && uf.TrySetAuto(false) && uf.TryGetValue(out var ufv))
        { focusAt = (uint)Math.Max(0, ufv); focusLocked = true; }
        if (!exposureLocked && UvcExposure is { } ue && ue.TrySetAuto(false) && ue.TryGetValue(out var uev))
        { exposureAtS = UvcExposureToSeconds(uev); exposureLocked = true; }

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
        try { UvcExposure?.TrySetAuto(true); UvcWhiteBalance?.TrySetAuto(true); UvcFocus?.TrySetAuto(true); } catch { }

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
        try { UvcExposure?.TrySetAuto(true); UvcWhiteBalance?.TrySetAuto(true); UvcFocus?.TrySetAuto(true); } catch { }
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
    /// <summary>Camera auto mode as read back from the device (null = unknown / not applicable).
    /// Lets the metadata state whether exposure was auto or manual instead of fixed text.</summary>
    public bool? AutoModeActive { get; init; }

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
