# vatSys Plugin Manager

A Windows desktop app that installs, updates, disables and removes vatSys plugins published by [Jurinas-vatsys-plugins](https://github.com/orgs/Jurinas-vatsys-plugins/repositories).

## Download

Get `vatSysPluginsManager.exe` from the [latest release](https://github.com/Jurinas-vatsys-plugins/plugins-manager/releases/latest). It is a single self-contained executable, so you don't need to install .NET.

## Features

- Install plugins into the global vatSys folder (`<vatSys>\bin\Plugins`) or into one profile (`Documents\vatSys Files\Profiles\<profile>\Plugins`)
- Shows when installed plugins have updates
- Disable/enable plugins (renames DLLs to `*.dll.disabled`) and remove them, with a confirmation prompt
- Unblocks plugin DLLs automatically (same as PowerShell `Unblock-File`), so vatSys can load downloaded files

Plugin versions come from the Jurina API (`https://api.jurina.org/v1/vatsys-plugins/plugins`), which checks GitHub hourly. Files are downloaded directly from the GitHub releases.

## Build

```bash
dotnet publish PluginsManager -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```
