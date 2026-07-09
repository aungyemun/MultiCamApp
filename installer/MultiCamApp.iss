; MultiCamApp installer — build with installer\build_release.bat
; Fully offline, self-contained bundle. Supports in-place upgrade without manual uninstall.
#define AppName "MultiCamApp"
#ifndef AppVersion
#define AppVersion "2.0.2"
#endif
#define AppPublisher "Aung Ye Mun"
; Numeric-only version for Windows VersionInfoVersion (no alpha/beta suffix allowed)
#define AppVersionNumeric "2.0.2.335"
#define AppExeName "MultiCamApp.exe"
#ifndef PublishDir
#define PublishDir "..\dist"
#endif
#define AppIcon "..\source\MultiCamApp\MultiCamApp\assets\icons\MultiCamApp.ico"
#define LicenseFile "..\docs\license\LICENSE.txt"

[Setup]
AppId={{A8C4E2B1-9F3D-4A6E-B5C1-2D8E7F0A3B4C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026-Present Aung Ye Mun and contributors
AppPublisherURL=
AppSupportURL=
AppUpdatesURL=
VersionInfoVersion={#AppVersionNumeric}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Multi-camera recording (VideoEngineV2/MediaFoundation), hardware calibration lock, per-camera metadata, and Video Verification
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersionNumeric}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UsePreviousAppDir=yes
DisableProgramGroupPage=no
OutputDir=.
OutputBaseFilename=MultiCamApp_{#AppVersion}_Setup
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CreateUninstallRegKey=yes
UninstallDisplayName={#AppName}
LicenseFile={#LicenseFile}
InfoBeforeFile=
ChangesAssociations=no
RestartIfNeededByRun=no
SetupLogging=yes
CloseApplications=force
CloseApplicationsFilter={#AppExeName},MultiCamApp.dll

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
UpgradeNotice=Existing MultiCamApp files will be replaced with this version. Your recorded videos and user data will be preserved.
UpgradeDetected=An existing MultiCamApp installation was detected in the selected folder. Setup will back up key settings, remove old application files, and install version {#AppVersion}.

[Tasks]
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce
Name: "startmenuicon"; Description: "Create Start Menu shortcuts"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#PublishDir}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#LicenseFile}"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "run_external4_preview_stress.bat,*.pdb,*.pyc,*.cache,*.tmp,*.bak,*.log,*.user,*.orig,*.mp4,*.avi,*.mov,*.mkv,*.wmv,*.webm,*.m4v,logs,backup_before_update,user_settings,config_user,debug,test,tests,tmp,temp,__pycache__"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} Diagnostic Launch"; Filename: "{app}\runtime\run_app_debug.bat"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} Offline Diagnostic"; Filename: "{app}\runtime\run_offline_diagnostic.bat"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon; BeforeInstall: RemoveExistingDesktopShortcuts

[Run]
Filename: "{cmd}"; Parameters: "/c ""{app}\runtime\setup_runtime.bat"" install"; WorkingDir: "{app}"; StatusMsg: "Configuring bundled runtimes..."; Flags: waituntilterminated; BeforeInstall: RunVCRedistBeforeRuntime; Check: ShouldRunRuntimeSetup

[InstallDelete]
; Pre-install cleanup on upgrade only (before new files are copied). Preserves logs, backups, user_settings, config_user, and video files.
Type: filesandordirs; Name: "{app}\runtime"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\assets"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\config"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\localization"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\tools"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\scripts"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\lib"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\platform"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\resources"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\app"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\_internal"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\cs"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\de"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\es"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\fr"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\it"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\ja"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\ko"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\pl"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\pt-BR"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\ru"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\tr"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\zh-Hans"; Check: IsUpgradeInstall
Type: filesandordirs; Name: "{app}\zh-Hant"; Check: IsUpgradeInstall
Type: files; Name: "{app}\*.dll"; Check: IsUpgradeInstall
Type: files; Name: "{app}\*.deps.json"; Check: IsUpgradeInstall
Type: files; Name: "{app}\*.runtimeconfig.json"; Check: IsUpgradeInstall
Type: files; Name: "{app}\{#AppExeName}"; Check: IsUpgradeInstall
Type: files; Name: "{app}\createdump.exe"; Check: IsUpgradeInstall

[UninstallDelete]
Type: filesandordirs; Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\*.exe"
Type: filesandordirs; Name: "{app}\*.json"
Type: filesandordirs; Name: "{app}\runtime"
Type: filesandordirs; Name: "{app}\logs"
Type: dirifempty; Name: "{app}"

[Code]
var
  VCRedistExitCode: Integer;
  RuntimeSetupExitCode: Integer;
  InstallLogPath: string;
  UpdateLogPath: string;
  UpgradeBackupDir: string;
  UpgradeMode: Boolean;

function FinalChecksPass(): Boolean; forward;
function IsVCRedistSuccess(Code: Integer): Boolean; forward;
procedure RunVCRedistBeforeRuntime(); forward;
function ShouldRunRuntimeSetup(): Boolean; forward;
function ReadRuntimeSetupExitCode(): Integer; forward;
procedure RemoveExistingDesktopShortcuts(); forward;

procedure RemoveExistingDesktopShortcuts();
var
  ShortcutPath: string;
begin
  ShortcutPath := ExpandConstant('{userdesktop}\{#AppName}.lnk');
  if FileExists(ShortcutPath) then
    DeleteFile(ShortcutPath);
  { Legacy installers used Public Desktop; remove so only one shortcut remains. }
  ShortcutPath := ExpandConstant('{commondesktop}\{#AppName}.lnk');
  if FileExists(ShortcutPath) then
    DeleteFile(ShortcutPath);
end;

procedure AppendToFile(const LogPath, Text: string);
begin
  if LogPath = '' then Exit;
  SaveStringToFile(LogPath, GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + ' ' + Text + #13#10, True);
end;

procedure AppendToInstallLog(Text: string);
begin
  AppendToFile(InstallLogPath, Text);
end;

procedure AppendToUpdateLog(Text: string);
begin
  AppendToFile(UpdateLogPath, Text);
  if UpgradeMode then
    AppendToInstallLog('[update] ' + Text);
end;

function EnsureInstallLogDir(): Boolean;
var
  LogDir: string;
begin
  LogDir := ExpandConstant('{app}\logs');
  Result := DirExists(LogDir) or ForceDirectories(LogDir);
  if Result then
  begin
    InstallLogPath := LogDir + '\install.log';
    UpdateLogPath := LogDir + '\install_update.log';
  end;
  { If log dir cannot be created, continue silently without installer logging. }
end;

function IsUpgradeInstall(): Boolean;
var
  AppDir: string;
begin
  AppDir := ExpandConstant('{app}');
  Result := FileExists(AppDir + '\{#AppExeName}')
    or FileExists(AppDir + '\MultiCamApp.dll');
  UpgradeMode := Result;
end;

function IsMultiCamAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec(ExpandConstant('{cmd}'),
    '/c tasklist /FI "IMAGENAME eq {#AppExeName}" /NH | find /I "{#AppExeName}"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

function TerminateMultiCamApp(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /T /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if Result then
    Sleep(1500);
end;

function ReadOldVersion(): string;
var
  Content: AnsiString;
  AppDir: string;
begin
  Result := 'unknown';
  AppDir := ExpandConstant('{app}');
  if LoadStringFromFile(AppDir + '\config\version.json', Content) then
  begin
    if Pos('"version":', Content) > 0 then
      Result := 'see version.json backup';
  end;
end;

procedure BackupFileIfExists(const SourcePath, DestDir: string);
var
  FileName: string;
begin
  if not FileExists(SourcePath) then Exit;
  FileName := ExtractFileName(SourcePath);
  if CopyFile(SourcePath, DestDir + '\' + FileName, False) then
    AppendToUpdateLog('Backed up: ' + FileName)
  else
    AppendToUpdateLog('WARNING: Could not back up ' + SourcePath);
end;

procedure BackupExistingInstall();
var
  AppDir, BackupRoot, Timestamp: string;
begin
  if not IsUpgradeInstall() then Exit;
  if not EnsureInstallLogDir() then Exit;

  AppDir := ExpandConstant('{app}');
  Timestamp := GetDateTimeString('yyyymmdd_hhnnss', #0, #0);
  BackupRoot := AppDir + '\backup_before_update';
  UpgradeBackupDir := BackupRoot + '\' + Timestamp;
  if not DirExists(BackupRoot) then
    CreateDir(BackupRoot);
  if not DirExists(UpgradeBackupDir) then
    CreateDir(UpgradeBackupDir);

  AppendToUpdateLog('=== Upgrade backup started ===');
  AppendToUpdateLog('Previous version marker: ' + ReadOldVersion());
  AppendToUpdateLog('Backup folder: ' + UpgradeBackupDir);

  BackupFileIfExists(AppDir + '\config\version.json', UpgradeBackupDir);
  BackupFileIfExists(AppDir + '\config\appsettings.json', UpgradeBackupDir);
  BackupFileIfExists(AppDir + '\logs\install.log', UpgradeBackupDir);
  BackupFileIfExists(AppDir + '\logs\install_update.log', UpgradeBackupDir);
  BackupFileIfExists(AppDir + '\logs\runtime_setup.log', UpgradeBackupDir);
  BackupFileIfExists(AppDir + '\user_settings\appsettings.user.json', UpgradeBackupDir);
  BackupFileIfExists(AppDir + '\config_user\appsettings.json', UpgradeBackupDir);

  AppendToUpdateLog('=== Upgrade backup completed ===');
end;

procedure CleanRootExecutables();
var
  AppDir: string;
  FindRec: TFindRec;
begin
  AppDir := AddBackslash(ExpandConstant('{app}'));
  if FindFirst(AppDir + '*.exe', FindRec) then
  try
    repeat
      if (Pos('unins', LowerCase(FindRec.Name)) <> 1) and (CompareText(FindRec.Name, '{#AppExeName}') <> 0) then
      begin
        if DeleteFile(AppDir + FindRec.Name) then
          AppendToUpdateLog('Removed old executable: ' + FindRec.Name);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure LogUpgradeCleanup();
begin
  if not UpgradeMode then Exit;
  AppendToUpdateLog('=== Pre-install cleanup (InstallDelete + managed patterns) ===');
  AppendToUpdateLog('Removed managed folders: runtime, assets, config, localization, locale packs, tools, scripts');
  AppendToUpdateLog('Removed managed files: *.dll, *.deps.json, *.runtimeconfig.json, {#AppExeName}');
  AppendToUpdateLog('Preserved: logs\, backup_before_update\, user_settings\, config_user\, video files');
  CleanRootExecutables();
end;

function EnsureAppClosedForInstall(): Boolean;
begin
  Result := True;
  if not IsMultiCamAppRunning() then Exit;

  if MsgBox('MultiCamApp is currently running. Please close it to continue installation.' + #13#10 + #13#10 +
    'Click Yes to close MultiCamApp automatically, or No to cancel setup.',
    mbConfirmation, MB_YESNO) = IDNO then
  begin
    Result := False;
    Exit;
  end;

  if not TerminateMultiCamApp() then
  begin
    MsgBox('Could not close MultiCamApp automatically. Please close it manually and run setup again.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if IsMultiCamAppRunning() then
  begin
    MsgBox('MultiCamApp is still running. Please close it manually and run setup again.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;

  if not EnsureAppClosedForInstall() then
  begin
    Result := '{#AppName} is running.';
    Exit;
  end;

  if IsUpgradeInstall() then
  begin
    BackupExistingInstall();
    AppendToUpdateLog('=== Upgrade install started: target version {#AppVersion} ===');
    LogUpgradeCleanup();
  end
  else if EnsureInstallLogDir() then
    AppendToUpdateLog('=== Fresh install started: version {#AppVersion} ===');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    if IsUpgradeInstall() then
    begin
      WizardForm.ReadyMemo.Lines.Add('');
      WizardForm.ReadyMemo.Lines.Add(ExpandConstant('{cm:UpgradeNotice}'));
      WizardForm.ReadyMemo.Lines.Add(ExpandConstant('{cm:UpgradeDetected}'));
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    if UpgradeMode then
      AppendToUpdateLog('Copying new version {#AppVersion} files...');
  end
  else if CurStep = ssPostInstall then
  begin
    RuntimeSetupExitCode := ReadRuntimeSetupExitCode();
    if not FinalChecksPass() then
      WizardForm.StatusLabel.Caption := 'Installation verification reported issues. See logs\install_update.log';
  end;
end;

function IsVCRedistSuccess(Code: Integer): Boolean;
begin
  { 0 = installed; 3010 = installed, reboot required; 1638 = equal/newer already installed }
  Result := (Code = 0) or (Code = 3010) or (Code = 1638);
end;

procedure RunVCRedistBeforeRuntime();
var
  ResultCode: Integer;
begin
  if not EnsureInstallLogDir() then
    Exit;
  if not FileExists(ExpandConstant('{tmp}\vc_redist.x64.exe')) then
  begin
    VCRedistExitCode := 0;
    AppendToInstallLog('VC++ Redistributable bundle not present; skipping.');
    AppendToUpdateLog('VC++ Redistributable bundle not present; skipping.');
    exit;
  end;
  WizardForm.StatusLabel.Caption := 'Installing Microsoft Visual C++ Runtime (offline bundle)...';
  AppendToInstallLog('Starting VC++ Redistributable installation...');
  AppendToUpdateLog('Starting VC++ Redistributable installation...');
  if Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'), '/quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    VCRedistExitCode := ResultCode;
    AppendToInstallLog('VC++ Redistributable finished with exit code: ' + IntToStr(ResultCode));
    AppendToUpdateLog('VC++ Redistributable finished with exit code: ' + IntToStr(ResultCode));
    if IsVCRedistSuccess(ResultCode) then
    begin
      if ResultCode = 1638 then
      begin
        AppendToInstallLog('OK: VC++ Redistributable already installed (exit 1638).');
        AppendToUpdateLog('OK: VC++ Redistributable already installed (exit 1638).');
      end;
    end
    else
    begin
      AppendToInstallLog('WARNING: VC++ Redistributable returned unexpected exit code ' + IntToStr(ResultCode) + '.');
      AppendToUpdateLog('WARNING: VC++ Redistributable returned unexpected exit code ' + IntToStr(ResultCode) + '.');
    end;
  end
  else
  begin
    VCRedistExitCode := -1;
    AppendToInstallLog('VC++ Redistributable failed to start.');
    AppendToUpdateLog('VC++ Redistributable failed to start.');
  end;
end;

function ShouldRunRuntimeSetup(): Boolean;
begin
  if not EnsureInstallLogDir() then
  begin
    Result := False;
    exit;
  end;
  AppendToInstallLog('Runtime setup scheduled once via [Run] (setup_runtime.bat install).');
  AppendToUpdateLog('Runtime setup scheduled once via [Run] (setup_runtime.bat install).');
  Result := True;
end;

function ReadRuntimeSetupExitCode(): Integer;
var
  Content: AnsiString;
  Path: string;
begin
  Result := 0;
  Path := ExpandConstant('{app}\logs\runtime_setup_exit.txt');
  if FileExists(Path) and LoadStringFromFile(Path, Content) then
    Result := StrToIntDef(Trim(Content), 0);
end;

function VersionJsonMatchesInstaller(): Boolean;
var
  Content: AnsiString;
  VersionPath: string;
begin
  Result := False;
  VersionPath := ExpandConstant('{app}\config\version.json');
  if not FileExists(VersionPath) then Exit;
  if LoadStringFromFile(VersionPath, Content) then
    Result := (Pos('"' + '{#AppVersion}' + '"', Content) > 0);
end;

function FinalChecksPass(): Boolean;
var
  CoreSuccess: Boolean;
  SmokeTestWarning: Boolean;
  Retries: Integer;
  LogPath: string;
  ResultPath: string;
  LogContent: AnsiString;
  ResultContent: AnsiString;
  SmokeExitCode: Integer;
begin
  if not EnsureInstallLogDir() then
  begin
    Result := False;
    exit;
  end;
  CoreSuccess := True;
  SmokeTestWarning := False;
  AppendToInstallLog('Running final installation checks...');
  AppendToUpdateLog('Running final installation verification...');

  if not FileExists(ExpandConstant('{app}\{#AppExeName}')) then begin AppendToInstallLog('FAIL: single-file apphost missing'); AppendToUpdateLog('FAIL: single-file apphost missing'); CoreSuccess := False; end;
  { OpenCvSharp managed code is bundled into the single-file apphost. The native extern DLL must remain beside the app. }
  if not FileExists(ExpandConstant('{app}\OpenCvSharpExtern.dll')) then begin AppendToInstallLog('FAIL: OpenCvSharpExtern.dll missing'); AppendToUpdateLog('FAIL: OpenCvSharpExtern.dll missing'); CoreSuccess := False; end;
  if not FileExists(ExpandConstant('{app}\runtime\ffmpeg\ffprobe.exe')) then begin AppendToInstallLog('FAIL: ffprobe.exe missing'); AppendToUpdateLog('FAIL: ffprobe.exe missing'); CoreSuccess := False; end;
  { Full ffmpeg.exe — powers the on-demand Deep Verify per-frame MD5 duplicate-frame check. }
  if not FileExists(ExpandConstant('{app}\runtime\ffmpeg\ffmpeg.exe')) then begin AppendToInstallLog('FAIL: ffmpeg.exe missing'); AppendToUpdateLog('FAIL: ffmpeg.exe missing'); CoreSuccess := False; end;
  if not FileExists(ExpandConstant('{app}\config\appsettings.json')) then begin AppendToInstallLog('FAIL: appsettings.json missing'); AppendToUpdateLog('FAIL: appsettings.json missing'); CoreSuccess := False; end;
  if not FileExists(ExpandConstant('{app}\localization\en.json')) then begin AppendToInstallLog('FAIL: en.json missing'); AppendToUpdateLog('FAIL: en.json missing'); CoreSuccess := False; end;
  if not FileExists(ExpandConstant('{app}\assets\icons\MultiCamApp.ico')) then begin AppendToInstallLog('FAIL: MultiCamApp.ico missing'); AppendToUpdateLog('FAIL: MultiCamApp.ico missing'); CoreSuccess := False; end;
  if not FileExists(ExpandConstant('{app}\runtime\run_offline_diagnostic.bat')) then begin AppendToInstallLog('FAIL: run_offline_diagnostic.bat missing'); AppendToUpdateLog('FAIL: run_offline_diagnostic.bat missing'); CoreSuccess := False; end;
  if not FileExists(ExpandConstant('{app}\runtime\runtime_initialized.flag')) then begin AppendToInstallLog('FAIL: runtime_initialized.flag missing'); AppendToUpdateLog('FAIL: runtime_initialized.flag missing'); CoreSuccess := False; end;
  if not FileExists(ExpandConstant('{app}\runtime\runtime_paths.env')) then begin AppendToInstallLog('FAIL: runtime_paths.env missing'); AppendToUpdateLog('FAIL: runtime_paths.env missing'); CoreSuccess := False; end;

  if not VersionJsonMatchesInstaller() then
  begin
    AppendToInstallLog('FAIL: config\version.json does not match installer version {#AppVersion}');
    AppendToUpdateLog('FAIL: config\version.json does not match installer version {#AppVersion}');
    CoreSuccess := False;
  end
  else
    AppendToUpdateLog('OK: config\version.json matches {#AppVersion}');

  LogPath := ExpandConstant('{app}\logs\runtime_setup.log');
  Retries := 0;
  while (Retries < 20) and (not FileExists(LogPath)) do
  begin
    Sleep(500);
    Retries := Retries + 1;
  end;

  if not FileExists(LogPath) then
  begin
    AppendToInstallLog('FAIL: runtime_setup.log missing at ' + LogPath);
    AppendToUpdateLog('FAIL: runtime_setup.log missing at ' + LogPath);
    CoreSuccess := False;
  end
  else
  begin
    if LoadStringFromFile(LogPath, LogContent) then
    begin
      if Pos('Runtime setup completed successfully', LogContent) > 0 then
      begin
        AppendToInstallLog('OK: runtime_setup.log verified success.');
        AppendToUpdateLog('OK: runtime_setup.log verified success.');
      end
      else
      begin
        AppendToInstallLog('FAIL: runtime_setup.log does not contain success message.');
        AppendToUpdateLog('FAIL: runtime_setup.log does not contain success message.');
        CoreSuccess := False;
      end;
    end
    else
    begin
      AppendToInstallLog('FAIL: Could not read runtime_setup.log');
      AppendToUpdateLog('FAIL: Could not read runtime_setup.log');
      CoreSuccess := False;
    end;
  end;

  if RuntimeSetupExitCode <> 0 then begin AppendToInstallLog('FAIL: setup_runtime.bat returned ' + IntToStr(RuntimeSetupExitCode)); AppendToUpdateLog('FAIL: setup_runtime.bat returned ' + IntToStr(RuntimeSetupExitCode)); CoreSuccess := False; end;
  if not IsVCRedistSuccess(VCRedistExitCode) then begin AppendToInstallLog('FAIL: vc_redist.x64.exe returned ' + IntToStr(VCRedistExitCode)); AppendToUpdateLog('FAIL: vc_redist.x64.exe returned ' + IntToStr(VCRedistExitCode)); CoreSuccess := False; end
  else if VCRedistExitCode = 1638 then AppendToUpdateLog('OK: VC++ Redistributable already installed (exit 1638).');

  if CoreSuccess then
  begin
    WizardForm.StatusLabel.Caption := 'Running offline compatibility verification...';
    AppendToInstallLog('Running app smoke test (--smoke-test)...');
    AppendToUpdateLog('Running app smoke test (--smoke-test)...');
    SmokeExitCode := 0;
    if Exec(ExpandConstant('{app}\{#AppExeName}'), '--smoke-test', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, SmokeExitCode) then
    begin
      AppendToInstallLog('Smoke test process exit code: ' + IntToStr(SmokeExitCode));
      AppendToUpdateLog('Smoke test process exit code: ' + IntToStr(SmokeExitCode));
    end
    else
    begin
      AppendToInstallLog('WARNING: Failed to start smoke-test process.');
      AppendToUpdateLog('WARNING: Failed to start smoke-test process.');
      SmokeTestWarning := True;
    end;

    ResultPath := ExpandConstant('{app}\logs\smoke_test_result.txt');
    if FileExists(ResultPath) then
    begin
      if LoadStringFromFile(ResultPath, ResultContent) then
      begin
        if Pos('PASS', ResultContent) > 0 then
        begin
          AppendToInstallLog('OK: Smoke test PASSED.');
          AppendToUpdateLog('OK: Smoke test PASSED.');
        end
        else if Pos('WARNING', ResultContent) > 0 then
        begin
          AppendToInstallLog('WARNING: Smoke test reported warning.');
          AppendToUpdateLog('WARNING: Smoke test reported warning.');
          SmokeTestWarning := True;
        end
        else
        begin
          AppendToInstallLog('FAIL: Smoke test reported failure.');
          AppendToUpdateLog('FAIL: Smoke test reported failure.');
          SmokeTestWarning := True;
        end;
      end;
    end
    else
    begin
      AppendToInstallLog('WARNING: smoke_test_result.txt missing (app may log to %LOCALAPPDATA%\MultiCamApp\logs).');
      AppendToUpdateLog('WARNING: smoke_test_result.txt missing (app may log to %LOCALAPPDATA%\MultiCamApp\logs).');
      SmokeTestWarning := True;
    end;
  end;

  if CoreSuccess then
  begin
    AppendToInstallLog('Core installation passed.');
    if UpgradeMode then
      AppendToUpdateLog('=== Upgrade completed successfully: {#AppVersion} ===')
    else
      AppendToUpdateLog('=== Fresh install completed successfully: {#AppVersion} ===');
    if SmokeTestWarning then
      AppendToUpdateLog('WARNING: Compatibility verification reported a warning.')
    else
      AppendToUpdateLog('Installation verification passed.');
    Result := True;
  end
  else
  begin
    AppendToInstallLog('Final checks FAILED (Core installation incomplete).');
    AppendToUpdateLog('=== Installation verification FAILED ===');
    MsgBox('MultiCamApp installation did not complete correctly.' + #13#10 + #13#10 +
           'Reason: Critical dependency, version mismatch, or setup failure.' + #13#10 +
           'Please check ' + ExpandConstant('{app}') + '\logs\install_update.log for details.', mbError, MB_OK);
    Result := False;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := MsgBox('This will remove MultiCamApp and all application components installed by Setup.exe. Your recorded videos and exported project files will not be removed. Do you want to continue?', mbConfirmation, MB_YESNO) = IDYES;
end;

procedure DeinitializeUninstall();
begin
end;





