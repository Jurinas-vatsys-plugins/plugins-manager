# vatSys Plugin Manager

A Windows desktop app that installs, updates, disables and removes vatSys plugins published by [Jurinas-vatsys-plugins](https://github.com/orgs/Jurinas-vatsys-plugins/repositories).

## Download

The [latest release](https://github.com/Jurinas-vatsys-plugins/plugins-manager/releases/latest) has two formats:

- `vatSysPluginsManager.exe`: a single self-contained executable, no .NET install needed (about 45 MB)
- `vatSysPluginsManager-framework-dependent.zip`: extract anywhere and run `vatSysPluginsManager.exe`; needs the [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (about 10 MB)

Each format updates itself from the same format.

## Features

- Install plugins into the global vatSys folder (`<vatSys>\bin\Plugins`) or into one profile (`Documents\vatSys Files\Profiles\<profile>\Plugins`)
- Recognises plugins installed by hand (by comparing DLLs with the release files) and shows when any copy has an update
- Warns when the same plugin is enabled both in the vatSys folder and in a profile, since vatSys then loads it twice
- Disable/enable plugins (renames DLLs to `*.dll.disabled`) and remove them, with a confirmation prompt
- Install from ZIP: installs a ZIP of an organisation plugin downloaded elsewhere, after a disclaimer that the safety of its files is not guaranteed; other ZIPs and source-code archives are refused
- Offers to restart as administrator when Windows denies access to a plugin folder (e.g. vatSys under Program Files)
- Updates itself: when a new release is out, **Update now** downloads the asset of the format you run, swaps the files and restarts
- Unblocks plugin DLLs automatically (same as PowerShell `Unblock-File`), so vatSys can load downloaded files

Plugin versions come from the Jurina API (`https://api.jurina.org/v1/vatsys-plugins/plugins`), which checks GitHub hourly. Files are downloaded directly from the GitHub releases.

## Build

```bash
dotnet publish PluginsManager -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Local builds do not update themselves; the release workflow sets `-p:Distribution=exe` or `-p:Distribution=zip` so each build knows which asset to update from.

## Release

Pushing a tag `v*` runs [.github/workflows/release.yml](.github/workflows/release.yml), which publishes the single-file exe and the framework-dependent zip and creates the GitHub release; tags with a suffix (`v0.3.0-beta.1`) become pre-releases. It can also be started from the Actions tab with a tag and a pre-release checkbox.
