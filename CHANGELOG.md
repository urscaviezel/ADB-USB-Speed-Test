# Changelog

## v1.6

- Added embedded application icon
- Added icon to the application title bar, taskbar and Alt+Tab
- Improved responsive window layout
- Increased default startup window size
- Default test duration changed to 600 seconds
- Maximum stable transfer-rate mode is selected on startup
- Added persistent language, TCP port, target rate and duration settings
- Added ADB cleanup after test completion, cancellation, errors and application exit
- Native .NET 8 WinForms implementation
- Self-contained Windows x64 publishing

## Earlier development

The project originally started as a Python/Tkinter prototype. During testing,
the transfer benchmark proved sensitive to certain Python packaging/runtime
launch methods. The application was therefore rewritten as a native .NET
WinForms application for reliable standalone Windows distribution.
