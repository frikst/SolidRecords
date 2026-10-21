# SolidRecords

English | [Slovenčina](README.sk.md)

Local computer activity tracking for Windows, built with **.NET 10 / WPF**. The app runs in the system tray and stores events and time intervals in SQLite. The interface supports English and Slovak.

## Installation

The repository's GitHub Releases provide an MSI installer and a portable ZIP for Windows x64 with the .NET runtime included. Installing the MSI requires administrator access and adds SolidRecords to the Start menu. **Launch SolidRecords after installation** is enabled on the final page; you can clear it before finishing. Running the app itself does not require administrator access. Before a manual update, exit the app through the tray menu; before uninstalling, also disable startup at sign-in. Uninstalling keeps local history and settings.

## Running

Building requires Windows 10/11 and the .NET 10 SDK. Framework-dependent publications require the .NET 10 Desktop Runtime.

```powershell
dotnet build SolidRecords.slnx
dotnet run --project src/SolidRecords.App -- --show
```

By default the app starts in the tray and begins recording automatically. `--show` always opens the overview. Double-click the tray icon or select **Open SolidRecords** to show it. Closing the window keeps the app running; **Exit application** stops capture, saves queued events and exits.

Start and stop recording using the window button or the tray menu. Toggling recording does not change automatic recording on the next launch.

Right-click the tray icon and open **Settings…**. **Save** stores all preferences in the database; **Cancel** discards edits.

| Setting | Default |
| --- | --- |
| Minimum activity duration | 15 seconds; range 0–600, with 0 disabling the minimum. |
| Automatically delete activity records | Never; optionally after a positive number of days. |
| Automatic recording | On launch – continuously; optionally during work hours only or off (manual). |
| Work hours and days | 07:00–15:00, Monday to Friday. |
| Start when signing in to Windows | Enabled. |
| Start minimized in the system tray | Enabled. |
| Language | System default; English for other system languages. English and Slovenčina can be selected explicitly. |
| Theme | System default; optionally light or dark. |
| Global keyboard shortcuts | Not set; optional shortcuts to show/hide the window and start/stop recording. |
| Daily update checks | Enabled; can be disabled. |

Change the language in **Settings → General**. Language, theme and minimum duration apply after saving without restarting. Changing the minimum completes the current activity and continues with the new threshold. It also affects the history view but cannot restore short activities that were never saved. Startup preferences take effect on the next launch. Automatic startup uses the current application path in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so launch the portable app from its final directory.

On the **Shortcuts** tab, click a field and press Ctrl, Alt or Win with a letter, digit or F1–F24; Shift is optional. Delete or Backspace clears the shortcut. Saved shortcuts work with the window hidden and outside the app. Combinations in use or reserved by Windows are rejected; choose another one. Shortcut actions are suspended while settings are open.

With **During work hours only**, recording starts and stops at the configured local times on selected days, including with the window hidden. Saving a changed schedule or recording mode immediately adjusts recording. A manual toggle lasts until the next work hours boundary. Times must belong to one day; the end can be 24:00. Selecting no days leaves no work hours. On working days the timeline covers at least the configured hours, even without activities or in another recording mode; earlier or later activities expand it. The schedule does not change daily totals of actual activity.

**About** in the tray menu shows the version, author, organization and a link to the department website. Esc also closes the dialog.

## Updates

The app checks GitHub Releases once every 24 hours. A new version triggers a Windows notification with **Download and install**, **In 4 hours** or **Ignore this version**. Deferrals and ignored versions persist across restarts. Disable automatic checks in **Settings → Updates**. **Update now** checks for a new version and downloads and installs it when available, even with daily checks disabled. The result appears on that tab.

After downloading and verifying the MSI, Windows requests administrator access. The app saves recording, exits and installs the update without further prompts or an automatic computer restart. After a successful update, the installed app restarts as the original user without administrator access and follows the saved display and recording preferences. Declining elevation leaves recording running. The portable version offers the same MSI, creating an installation in Program Files without replacing the portable directory. When the installed app starts automatically, startup at sign-in is updated to the new path according to the saved preference.

## Features

- Active and inactive user sessions: sign-in, sign-out, lock, unlock, RDP connection and disconnection, as well as suspend and resume.
- Process lifetimes inferred from windows, from the first active window to the last tracked window closing.
- Window activation and deactivation, title changes and available URL changes.
- Composite storage: process → windows → active window or URL spans. The flat activity list shows PID, document/window title, URL and times. Session changes appear as time separators; internal events remain hidden.
- The system theme follows Windows app mode while running; settings also offer fixed light and dark themes.
- A daily timeline colored by application, total active window time, and first and last activity times.
- Day selection and search by application, document or URL. The list, totals and timeline update after records are saved. Open activities show a dash for end and duration until completed; daily totals and the timeline continue updating. No manual refresh is needed. Additional filters are not yet available.
- An optional Details panel shows an application, its processes and all their activities for the selected day. There are no checkboxes or features for adding, merging or deleting timetracking items.

Totals include only leaf activities: at any moment, either the active window or its active URL counts, never both. Process and window lifetimes are not added together. Background applications, minimized windows and gaps between activations add no work time. This is not keyboard/mouse idle detection. Locking, disconnecting and suspending close the active span; parent records may remain open. Daily reports clip intervals to local midnight, including daylight saving transitions. Search filters the list; daily totals and the timeline cover the entire day.

Activities shorter than the configured minimum (**15 seconds** by default) are neither saved nor shown. An open activity appears once it reaches the threshold, retaining its original start time. Every individual active span must meet the threshold; several short visits cannot combine to qualify. If removing short activities leaves two identical activities next to each other with a gap **shorter than 15 seconds**, they are saved as one. Exactly 15 seconds of activity qualifies; a gap of exactly 15 seconds does not merge. Identity includes the process and window records, document title and URL; different program instances do not merge.

Merging preserves actual active spans: 20 seconds of work + a 5-second break + 20 seconds of work becomes one 40-second record. The break remains visible on the timeline. The same rules apply when displaying older history without bulk deletion. Minimums are evaluated before clipping to the day, preserving valid activity across midnight. The minimum can be changed in settings; the merge threshold remains 15 seconds.

## Capture limits

The app runs without administrator access. It observes only its own session and only while recording is enabled. Launching it again opens the running instance. System and helper windows (menus, taskbar, task switcher, lock screen) are excluded; time spent there forms a break.

URLs are read approximately once a second from the address bar of Chrome, Edge, Firefox, Brave, Opera and Vivaldi. Availability depends on the browser version, language, accessibility support and permissions. If no URL is available, the window title remains. Address bar text is not saved while being edited. Very short visits may be missed; this is not a complete browser history.

Forced termination or power loss may leave the last events unsaved. On the next launch, open activity ends at the last confirmed time, excluding downtime from work totals. A write error is displayed and the app stops reporting active recording; restart it after fixing the cause.

## Data

```text
%LOCALAPPDATA%\SolidRecords\activity.db
```

Data stays local and is never sent elsewhere. The database is not encrypted. On the Recording tab, enable automatic deletion of completed records after a chosen number of days; the default is **Never**. Deletion runs after saving settings, at startup and every hour. Active records and records overlapping the retained period are kept in full. Document titles and complete available URLs, including query parameters, are recorded.

Exit the app before making a file backup. Startup automatically upgrades the database schema; do not write to a migrated database with an older app version.

## License

SolidRecords is available under the **GNU General Public License, version 3 (GPL-3.0-only)**. The full text is in [LICENSE](LICENSE), also included with installed and portable copies. The app is provided without warranty as described in the license. Third-party components retain their own licenses.

## For developers

Start with [AGENTS.md](AGENTS.md), [architecture](doc/architecture.md), [recording rules](doc/recording.md), [storage and diagnostics](doc/storage.md), and the [development workflow](doc/development.md). These development documents are in Slovak.

## Publishing

Create a portable Windows x64 publication with the .NET runtime included:

```powershell
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Run `artifacts/publish/win-x64/SolidRecords.App.exe`. No installer is needed. On first launch, automatic startup is enabled for the current path by default.
