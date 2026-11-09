"""Content of the illustrated user-guide images (docs/images/guide/*.png).

One entry per screenshot card:
  source   raw capture name written by scripts/autotest/guide_screenshots.ps1 (<source>.png + <source>.json)
  crop     (x0, y0, x1, y1) in window pixels
  layout   "side" (screenshot left, explanations right) or "below" (explanations under the screenshot)
  eyebrow, title, lead   card heading
  tip      optional one-line tip shown in an amber box (side layout)
  items    (number, target, padding, badge position, title, description)
           target: element key from the JSON, a list of keys (their union) or a literal [x, y, w, h]
           badge position: corner | tr | left | top
Both annotate.py (markers on the screenshot) and render.py (cards) read this file, so numbers always match.
"""

LEFT_COLUMN = (0, 128, 500, 946)    # tabs + left settings panel of the main window

CARDS = {
    "step2_choose_cameras": dict(
        source="main_s0", crop=LEFT_COLUMN, layout="side",
        eyebrow="Step 2 of 8", title="Choose your cameras",
        lead="Plug in your webcams first, ideally each into its own USB port.",
        tip="Using several cameras at 720p or higher? Connect each one to its own USB port rather than to one hub.",
        items=[
            (1, ["radio:1 Camera", "radio:4 Cameras"], 8, "left", "How many cameras",
             "Choose a layout for 1, 2, 3 or 4 cameras."),
            (2, "Cam1Box", 6, "left", "Which camera",
             "Pick a camera for each slot. Its live picture appears on the right after Start Preview."),
            (3, "RefreshDevicesBtn", 4, "tr", "Refresh cameras",
             "Plugged in a camera after opening the app? Click here to update the list."),
        ]),
    "step3_picture_settings": dict(
        source="main_s60", crop=LEFT_COLUMN, layout="side",
        eyebrow="Step 3 of 8", title="Choose the picture settings",
        lead="Scroll down the left panel. If you are unsure, keep 720p and 30 fps.",
        tip="Moving horizontal bands under fluorescent or LED light? Set Anti-flicker to your mains frequency or to Auto.",
        items=[
            (1, "ResolutionBox", 6, "left", "Resolution",
             "The picture size. 720p is a good starting point; 1080p gives more detail and larger files."),
            (2, "FpsBox", 6, "left", "Frame rate",
             "Frames per second. 30 fps suits most recordings."),
            (3, "AdvancedCameraControlsExpander", 4, "left", "Advanced Camera Controls",
             "Optional: focus, exposure, anti-flicker and video quality. Leave the defaults unless you need them."),
        ]),
    "step4_save_location": dict(
        source="main_s100", crop=LEFT_COLUMN, layout="side",
        eyebrow="Step 4 of 8", title="Choose where to save",
        lead="Recordings stay on your computer. Nothing is uploaded.",
        tip="Nothing is uploaded. Your recordings stay in this folder on your computer.",
        items=[
            (1, ["OutputBox", "BrowseBtn"], 6, "left", "Output folder",
             "Your Videos folder, unless you click Browse... to choose another one."),
            (2, "SessionBox", 6, "left", "Session title",
             "Optional. A name for this recording, used in the folder name."),
            (3, "SessionFolderPreviewValue", 4, "left", "Final folder",
             "Every recording gets its own new folder, named with the title, date and time."),
        ]),
    "step5_preview_record": dict(
        source="main_s100", crop=LEFT_COLUMN, layout="side",
        eyebrow="Step 5 of 8", title="Preview, then record",
        lead="The live pictures appear on the right side of the window.",
        tip="Focus is locked when recording starts, so check that the picture is sharp in the preview first.",
        items=[
            (1, "StartPreviewBtn", 5, "left", "Start Preview",
             "Turns the cameras on. Check that every picture looks right and is in focus."),
            (2, "StartRecordBtn", 5, "tr", "Start Recording",
             "All selected cameras start recording together."),
            (3, "StopRecordBtn", 5, "tr", "Stop Recording",
             "Ends the recording and finishes the files."),
            (4, ["StatusSectionTitle", "StatusValue", "ElapsedValue", "CamerasValue"], 10, "left", "Status",
             "What the app is doing and how long it has been recording."),
        ]),
    "step7_verify": dict(
        source="verify_s0", crop=(0, 128, 1600, 410), layout="below",
        eyebrow="Step 7 of 8", title="Check your recordings",
        lead="Open the Video Verification tab. Everything is checked on your computer, offline.",
        items=[
            (1, "BrowseFolderBtn", 4, "corner", "Browse...",
             "Choose the folder that holds your recordings. The videos in it are found automatically."),
            (2, "VerifyAllBtn", 4, "corner", "Verify All",
             "Checks every video: frames, timing and files."),
            (3, "DeepVerifyBtn", 4, "tr", "Deep Verify",
             "Optional extra check of every frame and of picture stability. Takes longer."),
            (4, ["ExportTxtBtn", "ExportJsonBtn", "ExportCsvBtn", "ExportAllBtn"], 5, "corner", "Export",
             "Saves the report as a TXT, JSON or CSV file."),
        ]),
    "step8_results_summary": dict(
        source="verify_s0", crop=(0, 530, 1600, 912), layout="below",
        eyebrow="Step 8 of 8", title="Read the summary",
        lead="The top of the results gives one answer for the whole folder.",
        items=[
            (1, ["CardOverallLabel", "CardOverallValue"], 14, "tr", "Overall result",
             "The worst result among the videos in the folder."),
            (2, ["CardPassLabel", "CardPassValue", "CardFailLabel", "CardFailValue"], 14, "tr",
             "Passed, Warnings, Failed", "How many videos are in each group."),
            (3, ["ScientificTimingConfidenceLabel", "CardTimingConfidenceValue"], 14, "tr", "Timing confidence",
             "How reliable the timing is for scientific analysis."),
        ]),
    "step8_session_table": dict(
        source="verify_s50", crop=(0, 505, 1600, 935), layout="below",
        eyebrow="Step 8 of 8", title="Read the details per camera",
        lead="Further down, every recording session has its own table.",
        items=[
            (1, ["SimpleViewRadio", "DetailedViewRadio"], 6, "tr", "Simple or Detailed",
             "A short table, or one with every measurement."),
            (2, [25, 590, 470, 30], 2, "tr", "Session",
             "One section for each recording, with its result."),
            (3, [25, 840, 150, 75], 2, "tr", "Result per camera",
             "PASS, WARNING or FAIL for each camera."),
            (4, [893, 840, 242, 75], 2, "tr", "Frames",
             "Frames written and frames dropped. 0 dropped is what you want."),
        ]),
    "extra_hardware_diagnostics": dict(
        source="hardware", crop=(0, 128, 1600, 905), layout="below",
        eyebrow="Optional", title="Check your computer and cameras",
        lead="The Hardware Diagnostics tab helps when a camera is slow, missing or drops frames.",
        items=[
            (1, "RunScanButton", 5, "tr", "Run Hardware Scan",
             "Checks this computer and the connected cameras. Nothing is changed."),
            (2, ["StatusText", "RecommendedPresetText"], 6, "tr", "Summary",
             "Plain-language advice for your setup."),
            (3, ["UsbStatusCardTitleText", "UsbStatusCardText"], 12, "tr", "USB / Camera Connection",
             "Which USB connection each camera uses, with a warning when cameras share a hub."),
        ]),
    "extra_about": dict(
        source="about", crop=(0, 38, 950, 646), layout="side",
        eyebrow="Reference", title="Version, licence and citation",
        lead="Click About at the top right of the main window.",
        tip="The citation for papers is further down in the same window, with its own Copy button.",
        items=[
            (1, ["VersionText", "ReleaseText"], 8, "left", "Version",
             "Version, build number, release stage and date."),
            (2, "CopyVersionBtn", 5, "left", "Copy Version Info",
             "Copies the version details, useful when asking for help."),
            (3, ["LicenseUsageTitle", "CommercialUseBody"], 8, "left", "Licence and usage",
             "Free for non-commercial research, education and evaluation. Scroll down for the citation."),
        ]),
}
