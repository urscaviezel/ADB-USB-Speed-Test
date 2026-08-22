# ADB USB Speed Test

A native Windows GUI for testing ADB/USB transfer performance with Android and VR headsets.

![ADB USB Speed Test](screenshots/main-window.png)

## Features

- Native Windows x64 application built with .NET 8 WinForms
- No Python required on the target PC
- German / English interface
- Persistent settings
- ADB device detection
- Configurable TCP port
- Fixed-bandwidth test in Mbit/s
- Automatic maximum stable transfer-rate test
- Configurable test duration
- Live current and average throughput
- Transferred data counter
- Remaining time and progress
- Automatic retry with reduced bandwidth after a disconnect
- Test cancellation
- Automatic `adb kill-server` cleanup after tests and when the application closes
- Embedded application icon for Explorer, title bar, taskbar and Alt+Tab

## How it works

The application uses `adb reverse` to expose a local TCP listener to the connected Android device.  
On the headset/device, `toybox nc` receives the test stream and discards the data to `/dev/null`.

In **Maximum stable transfer rate** mode, the first attempt runs without a bandwidth limit. If the connection drops, the next attempt automatically uses a lower rate. This continues until the configured test duration completes successfully.

## Requirements

### Running the released application

- Windows 10/11 x64
- Android device or VR headset with USB debugging enabled
- Android Platform Tools (`adb.exe` and its required DLLs)

The application itself is published as a self-contained .NET 8 build, so the target PC does **not** need Python or a separately installed .NET runtime.

### ADB / Android Platform Tools

ADB is developed and distributed by Google as part of Android SDK Platform Tools.

Download the official Platform Tools from:

https://developer.android.com/tools/releases/platform-tools

Place the Platform Tools files in an `adb` folder next to `ADB_USB_Speed_Test.exe`:

```text
ADB_USB_Speed_Test/
├── ADB_USB_Speed_Test.exe
└── adb/
    ├── adb.exe
    ├── AdbWinApi.dll
    └── AdbWinUsbApi.dll
```

ADB binaries are intentionally not stored in this repository.

## Usage

1. Enable USB debugging on the Android/VR device.
2. Connect the device by USB.
3. Accept the USB-debugging authorization prompt on the device if required.
4. Start `ADB_USB_Speed_Test.exe`.
5. Click **Check HMD**.
6. Choose either:
   - **Determine maximum stable transfer rate**, or
   - **Limit bandwidth**
7. Set the desired test duration.
8. Click **Start**.

The default test duration is **600 seconds** and the application starts in **Maximum stable transfer rate** mode.

## Build from source

### Build requirements

- Windows x64
- .NET 8 SDK
- Android Platform Tools
- Optional: Inno Setup 6 for creating the installer

Clone the repository and place your local Platform Tools inside:

```text
build/adb/
```

Then run:

```powershell
build\BUILD_DOTNET8_PORTABLE.cmd
```

The self-contained portable build is created in:

```text
publish\ADB_USB_Speed_Test.exe
```

To create the installer, compile:

```text
build\installer.iss
```

with Inno Setup 6 after the portable build has completed.

## Repository structure

```text
ADB-USB-Speed-Test/
├── README.md
├── CHANGELOG.md
├── LICENSE
├── .gitignore
├── src/
│   └── ADB_USB_Speed_Test/
│       ├── Program.cs
│       ├── ADB_USB_Speed_Test.csproj
│       └── ADB_USB_Speed_Test.ico
├── build/
│   ├── BUILD_DOTNET8_PORTABLE.cmd
│   ├── BUILD_DOTNET8_PORTABLE.ps1
│   └── installer.iss
├── assets/
│   └── app-icon.png
└── screenshots/
    └── main-window.png
```

## License

This project is intended to be released under the **GNU General Public License v3.0 (GPL-3.0)**.

Android Debug Bridge (ADB) and Android SDK Platform Tools are separate Google/Android components and are not part of this project's source-code license.

## Author

Created by [@urscaviezel](https://github.com/urscaviezel).
