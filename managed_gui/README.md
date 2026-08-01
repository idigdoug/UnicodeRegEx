# managed_gui/ — UniRex (WinForms GUI)

*Layer 5 (GUI).* The primary end-user search-and-replace tool: a Windows Forms application built on
the shared tools library ([`managed_tools/`](../managed_tools/)). Produces **`UniRex.exe`**. Targets
.NET Framework 4.8.

The GUI binds its controls to a `SearchRequest` and runs `SearchJob`s from the Tools library, marshaling
the job's background-thread progress and results onto the UI thread. Preferences persist between
sessions; transient working state does not (the `SettingRole` classification in the Tools library drives
which is which).

## What's here

- **`MainForm`** — the main window: pattern/paths entry, the results list, status, and the run/apply
  flow.
- **`CoreSettingsPane` / `CollapsedSettingsPane`** — the always-visible core options and their collapsed
  summary.
- **`AdvancedSettingsForm`** — a property page generated from the Tools settings model
  (`SettingGroup`), surfacing the less-common options.
- **`ActionBar`** — the Apply / Select All / Select None / Cancel actions.
- **`OpenWithEditorForm`** — helper for opening a matched file in an external editor.
- **`Program.cs`** — the WinForms entry point.

## Dependencies

The Tools library ([`managed_tools/`](../managed_tools/)) and, through it, the interop wrapper and
native DLL. Windows Forms (.NET Framework 4.8).

## Part of

The [UnicodeRegEx](../README.md) project — layer 5.
