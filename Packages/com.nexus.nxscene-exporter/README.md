# NxScene Exporter (UPM)

Unity Editor exporter that generates a single `.nxscene` file (zip) containing platform AssetBundles + a `manifest.json` + `thumbnail.png`.

## Install (Git URL)

Add via Unity Package Manager:

- Window -> Package Manager
- + -> Add package from git URL...
- `https://github.com/Shisuiicaro/NxSceneExporter.git?path=/Packages/com.nexus.nxscene-exporter#main`

## Usage

Menu:

- Tools/Nexus/Export/Export Current Scene (.nxscene)
- Tools/Nexus/Export/Export Prefab Folder (.nxscene)

## Marketplace rules

- Export requires both **Windows (StandaloneWindows64)** and **Linux (StandaloneLinux64)** targets with **IL2CPP** scripting backend. OSX is not exported.
- The `.nxscene` is plaintext at export time; the Nexus website encrypts/splits it on upload.
