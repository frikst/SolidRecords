# Changelog

User-facing changes in SolidRecords.
The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).

## Unreleased

## [1.4.0] - 2026-10-21

### Added

- English and Slovak interfaces with a language selector in settings. The default follows the system language, with English for other languages. Saving applies the change without restarting.

### Fixed

- Calendar month and weekday names follow the selected interface language. The date field changes format immediately after switching languages, without opening the calendar or restarting.

- The Settings window has smaller margins, no repeated heading and all tabs on one row, leaving more room for their content.
- When a new day begins, the view automatically switches to the current date, including while browsing older history, with the window hidden or recording paused.
- On shutdown or sign-out, the reason is saved even if capture stops before receiving the Windows notification. The same notification is not shown twice.
- When recording first starts in the app, the active session is shown as “State at application startup”. The time represents the observed state rather than the exact sign-in time; resuming after a pause does not add another such row.

## [1.3.0] - 2026-10-19

### Added

- Optional global keyboard shortcuts for showing/hiding the window and starting/stopping recording, configured on the separate Shortcuts tab. Neither shortcut is set by default.
- The activity list includes session activation and deactivation separators with the captured event type, such as locking, unlocking, signing out or shutting down. The separator time aligns with the Start column.
- The installer offers to launch SolidRecords immediately after installation. This option is enabled by default and can be cleared before finishing.

### Fixed

- Periodic desktop checks no longer incorrectly mark a locked or disconnected session as active.
- Starting or resuming recording is no longer shown as session activation; redundant separators also disappear from older history.
- Activities remain readable in the dark theme, and the up/down arrow keys skip session separators when selecting activities.
- After an update started from the app succeeds, SolidRecords automatically restarts and continues using the saved settings.

## [1.2.0] - 2026-10-19

### Added

- The Details toggle beside search opens a right-hand tree of the application, its processes and all their activities for the selected day. The panel follows the selected activity even during searches.
- A separate Updates tab in settings with an “Update now” button to check for, download and install a new version manually, even when daily checks are disabled.

### Changed

- Text fields in settings match the rest of the app, with larger inner margins and the same height as dropdowns.

### Fixed

- Application details no longer list the same process more than once after pausing or restarting recording when its creation time is available. The fix also combines older observations while preserving activities and breaks.

## [1.1.0] - 2026-10-18

### Added

- Automatic deletion of older records after a configurable number of days. The default “Never” keeps history indefinitely.
- Configurable work hours and days (7:00–15:00, Monday to Friday by default), with optional automatic recording only during work hours. The timeline covers the full work period even without activities and expands for earlier or later work. Default recording remains unrestricted by time.
- Daily checks for new versions with a Windows notification: download and silent installation after administrator approval, a reminder in 4 hours, or ignoring a specific version. Checks can be disabled in settings.

### Changed

- Settings are split into General, Recording and Work hours tabs to keep the window smaller and easier to navigate.

## [1.0.0] - 2026-10-16

### Added

- A Settings window accessible from the tray menu: a 0–600 second activity minimum, automatic recording, startup at sign-in, starting in the tray, and system, light or dark themes. Preferences are saved with history; automatic recording and startup at sign-in are enabled by default.
- Application icons next to names in the activity list. Collected icons remain available after tracked applications close.
- Recording of used applications, open document titles and time spent in individual windows. The interface is in Slovak, and data stays on your computer without being sent anywhere.
- A daily overview with a colored timeline, total time in active windows, and the first and last activity times. The overview updates continuously and lets you browse previous days.
- Search by application, document title or web address. Daily totals and the timeline always cover the whole day, including during searches.
- Recording of visited web addresses in Chrome, Edge, Firefox, Brave, Opera and Vivaldi when exposed by the browser. If an address is unavailable, the window title remains. Text being typed in the address bar is not saved.
- Recording continues after the main window closes. Reopen the app using the tray icon and start or stop recording from the window or tray menu. Automatic recording on the next launch is configured separately in settings.
- Optional automatic startup when signing in to Windows. Launching the app again opens the window of the running instance.
- Totals include only time in the active window, excluding background applications. Locking, suspending or disconnecting Remote Desktop interrupts activity recording. Mouse movement and keyboard input are not yet used to detect inactivity.
- Brief switches to another window shorter than the configured minimum (15 seconds by default) are omitted. After omitting them, adjacent records of the same activity are combined if the gap is less than 15 seconds. Breaks do not count toward totals and remain visible on the timeline.
- System and helper windows, such as the Start menu or task switcher, are excluded from the work log.
- A compact window with light and dark appearances that automatically follow Windows settings.
- After a crash or power failure, the last activity is closed at its last saved time on the next launch. Time when the app was not running is therefore excluded from work totals.
- The app is available under GPL v3. The full license is included with the app and installer.
- An installer and portable ZIP for 64-bit Windows. Required components are bundled, so a separate .NET installation is unnecessary. Extract and run the portable version.

### Fixed

- Sorting by duration uses actual time. Clicking the same column header a third time clears sorting and restores newest-first order.
