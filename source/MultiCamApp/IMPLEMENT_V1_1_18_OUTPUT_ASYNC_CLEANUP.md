
K. Session transaction and finalization system
- Add a RecordingSessionTransaction service.
- Treat each recording session as a transaction with phases:
  - Created
  - Preparing
  - Recording
  - StopRequested
  - FinalizingVideos
  - RunningAudits
  - WritingMetadata
  - VerifyingOutputs
  - Completed
  - CompletedWithWarnings
  - Failed
- For every active camera slot, track finalization status independently:
  - MP4 finalized
  - timestamp CSV closed
  - ffprobe audit completed
  - metadata JSON written
  - metadata TXT written
  - visual quality audit completed
  - output manifest verified
- Session must not report Global PASS until the output manifest confirms all required files exist for every active slot.
- If one camera fails metadata writing, still write camX_metadata_error.txt.
- Add transaction recovery logic for incomplete previous sessions.

L. Camera lifecycle manager
- Add CameraLifecycleManager.
- Each camera must have lifecycle states:
  NotInitialized, PreviewStarting, Previewing, RecordingStarting, Recording, RecordingStopping, Finalizing, PreviewStopping, Releasing, Released, Error.
- Stop preview and stop recording must be idempotent.
- Add timeout handling and cancellation tokens.

M. Background task queue
- Add BackgroundTaskQueue or AppWorkQueue.
- Move camera release, ffprobe, visual QC, metadata writing, manifest writing, and cleanup away from UI thread.
- UI thread may only update progress/status.

N. Device identity registry
- Add DeviceIdentityRegistry.
- Map camera controls and recording sessions by unique device id/symbolic link, not friendly name.
- Detect duplicate friendly names and show unique labels in UI.

O. Runtime health/status monitor
- Track preview frame age, recording timestamp age, encoder active state, pending background tasks, stop latency, finalization latency, close latency, and task failures.
- Add [Runtime Health] section to metadata.

P. Release dependency manifest
- Add release_dependency_manifest.json and release_dependency_manifest.txt.
- List bundled DLLs/EXEs, ffmpeg/ffprobe, OpenCV presence, DirectShow fallback presence, .NET runtime mode, and debug/test scripts.
- Remove unused OpenCV/legacy files unless explicitly marked as legacy fallback.

Q. Crash-safe logging and diagnostics
- Add session logs:
  logs/session_runtime.log
  logs/camera_lifecycle.log
  logs/output_manifest.log
  logs/control_apply.log
- Log app start, preview start/stop, recording start/stop, finalization, metadata writing, close request, camera release, and exceptions.

R. Updated acceptance criteria
- Output manifest must pass before Global PASS.
- Stop recording and stop preview must be one-click and idempotent.
- UI freeze above 1000 ms during idle/stop/finalization/close should be treated as warning or fail depending on severity.
- Camera controls must report probe status with reason, not generic Unknown/Not attempted.
- Frame counters must distinguish captured, timestamped, and ffprobe-encoded frames.
- Duplicate camera names must be handled using unique device identity.
- Release bundle must not contain unused OpenCV/legacy files unless explicitly marked as fallback.
