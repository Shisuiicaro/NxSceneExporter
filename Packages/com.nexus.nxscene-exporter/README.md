# Nexus Scene Exporter

Exports standard URP `.nxscene` packages for local Nexus use.

Scripts are removed from export copies. Marketplace catalog metadata is added by Nexus owner publishing tools.

Windows and Linux bundles use chunk-based compression for fast local exports with smaller packages. Static meshes keep their original hierarchy.

Install through Unity Package Manager with:

`https://github.com/Shisuiicaro/NxSceneExporter.git?path=/Packages/com.nexus.nxscene-exporter#main`

The GitHub release includes `NxSceneExporter.zip`, containing the UPM package. Each published release tag must match the package version, such as `v2.1.1` for package version `2.1.1`.

Marketplace signing, encryption, and publishing are handled by Nexus owner tools.
