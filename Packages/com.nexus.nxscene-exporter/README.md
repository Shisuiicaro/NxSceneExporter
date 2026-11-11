# Nexus Scene Exporter

Exports standard URP `.nxscene` packages for local Nexus use.

Scripts are removed from export copies. Built-in URP Volume, camera data and light data are preserved. Marketplace catalog metadata is added by Nexus owner publishing tools.

VFX Graph components are not exported. The exporter reports them in the package diagnostics.

## Diagnostics

Open `Tools/Nexus/Inspect Scene Package` to inspect a `.nxscene` without the source project.

Every export writes `<package>.diagnostics.json` next to the `.nxscene` and includes `diagnostics.json` in the package. The report lists source and exported components, removed components, VFX Graph objects, particle systems, materials, shaders and custom vertex streams.

Windows and Linux bundles use chunk-based compression for fast local exports with smaller packages. Static meshes keep their original hierarchy.

Install through Unity Package Manager with:

`https://github.com/Shisuiicaro/NxSceneExporter.git?path=/Packages/com.nexus.nxscene-exporter#main`

The GitHub release includes `NxSceneExporter.zip`, containing the UPM package. Each published release tag must match the package version, such as `v2.1.8` for package version `2.1.8`.

Marketplace signing, encryption, and publishing are handled by Nexus owner tools.
