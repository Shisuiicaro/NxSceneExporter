#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Nexus.NxScene;

public static class NxSceneExporterEditor
{
    [MenuItem("Tools/Nexus/Export/Export Current Scene (.nxscene)", false, 2000)]
    [MenuItem("Tools/Nexus/Export/Export Current Scene (.nxscene)", false, 2000)]
    public static void ExportCurrentScene()
    {
        if (UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset != null)
        {
            EditorUtility.DisplayDialog("Pipeline Error", "URP/HDRP Detected. Please switch to the Built-in Render Pipeline for Nexus compatibility.", "OK");
            return;
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string sceneName = string.IsNullOrEmpty(scene.name) ? "Scene" : scene.name;

        string defaultName = sceneName + ".nxscene";
        string outputPath = EditorUtility.SaveFilePanel("Export NxScene", GetDefaultExportFolder(), defaultName, "nxscene");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "nxscene_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string tempAssetsDir = "Assets/Nexus/Temp/NxSceneExport";
        EnsureAssetFolder(tempAssetsDir);

        var createdTempAssets = new List<string>();
        List<GameObject> mergedObjects = null;

        try
        {
            var rawRoots = scene.GetRootGameObjects()
                .Where(go => go != null)
                .Where(go => go.hideFlags == HideFlags.None)
                .ToArray();

            // --- Merging Logic ---
            var finalExportRoots = new List<GameObject>();
            var objectsToMerge = new List<GameObject>();

            foreach (var root in rawRoots)
            {
                string layerName = LayerMask.LayerToName(root.layer);
                bool isStatic = root.isStatic; // Checks for any static flag.
                
                // Exclude special layers from merging
                bool isSpecialLayer = (layerName == "Door" || layerName == "Ground" || layerName == "Interactable");

                if (isStatic && !isSpecialLayer)
                {
                    objectsToMerge.Add(root);
                }
                else
                {
                    finalExportRoots.Add(root);
                }
            }

            mergedObjects = MergeStaticObjects(objectsToMerge, tempAssetsDir, createdTempAssets);
            finalExportRoots.AddRange(mergedObjects);
            // ---------------------

            var prefabPathByRoot = new Dictionary<GameObject, string>();
            int currentStep = 0;
            int totalSteps = finalExportRoots.Count;

            foreach (var root in finalExportRoots)
            {
                EditorUtility.DisplayProgressBar("Exporting NxScene", $"Processing {root.name}...", (float)currentStep / totalSteps);
                currentStep++;

                string prefabPath = ResolveOrCreatePrefabAssetPath(root, tempAssetsDir, createdTempAssets);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    continue;
                }
                prefabPathByRoot[root] = prefabPath;
            }

            var prefabEntriesByPath = new Dictionary<string, NxScenePrefab>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in prefabPathByRoot)
            {
                string prefabPath = kv.Value;
                if (prefabEntriesByPath.ContainsKey(prefabPath))
                {
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                string name = Path.GetFileNameWithoutExtension(prefabPath);

                prefabEntriesByPath[prefabPath] = new NxScenePrefab
                {
                    id = guid,
                    name = name,
                    bundles = new NxScenePlatformBundle[0]
                };
            }

            var prefabs = prefabEntriesByPath.Values.OrderBy(p => p.name).ToArray();
            var instances = new List<NxSceneInstance>();

            foreach (var root in finalExportRoots)
            {
                if (!prefabPathByRoot.TryGetValue(root, out var prefabPath))
                {
                    continue;
                }

                var prefab = prefabEntriesByPath[prefabPath];
                string layerName = LayerMask.LayerToName(root.layer);

                instances.Add(new NxSceneInstance
                {
                    name = root.name,
                    layer = layerName,
                    prefabId = prefab.id,
                    parentIndex = -1,
                    parentPath = null,
                    localPosition = root.transform.localPosition,
                    localRotation = root.transform.localRotation,
                    localScale = root.transform.localScale
                });
            }

            string bundlesRoot = Path.Combine(tempDir, "bundles");
            Directory.CreateDirectory(bundlesRoot);

            var builds = prefabs.Select(p => new AssetBundleBuild
            {
                assetBundleName = GetBundleFileName(p),
                assetNames = new[] { GetPrefabPathByGuid(prefabEntriesByPath, p.id) }
            }).ToArray();

            var manifestPrefabs = new List<NxScenePrefab>();
            foreach (var p in prefabs)
            {
                manifestPrefabs.Add(new NxScenePrefab
                {
                    id = p.id,
                    name = p.name,
                    bundles = new[]
                    {
                        new NxScenePlatformBundle { platform = "StandaloneWindows64", path = $"bundles/StandaloneWindows64/{GetBundleFileName(p)}" },
                        new NxScenePlatformBundle { platform = "StandaloneOSX", path = $"bundles/StandaloneOSX/{GetBundleFileName(p)}" },
                        new NxScenePlatformBundle { platform = "StandaloneLinux64", path = $"bundles/StandaloneLinux64/{GetBundleFileName(p)}" },
                    }
                });
            }

            EditorUtility.DisplayProgressBar("Exporting NxScene", "Building AssetBundles...", 1.0f);

            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneWindows64"), builds, BuildTarget.StandaloneWindows64);
            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneOSX"), builds, BuildTarget.StandaloneOSX);
            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneLinux64"), builds, BuildTarget.StandaloneLinux64);

            var manifest = new NxSceneManifest
            {
                version = "1",
                sceneName = sceneName,
                prefabs = manifestPrefabs.ToArray(),
                instances = instances.ToArray()
            };

            string manifestPath = Path.Combine(tempDir, "manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest));

            CreateZip(outputPath, tempDir, manifest);

            EditorUtility.ClearProgressBar();
            EditorUtility.RevealInFinder(outputPath);
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"NxScene Export Failed: {e}");
            EditorUtility.DisplayDialog("Export Failed", $"An error occurred: {e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            
            // Cleanup merged temporary objects in scene
            if (mergedObjects != null)
            {
                foreach (var obj in mergedObjects)
                {
                    if (obj != null)
                    {
                        UnityEngine.Object.DestroyImmediate(obj);
                    }
                }
            }

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
            }

            for (int i = 0; i < createdTempAssets.Count; i++)
            {
                string path = createdTempAssets[i];
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            AssetDatabase.Refresh();
        }
    }

    private static List<GameObject> MergeStaticObjects(List<GameObject> roots, string tempAssetsDir, List<string> createdTempAssets)
    {
        var result = new List<GameObject>();
        var groupedByMaterial = new Dictionary<Material, List<CombineInstance>>();
        var firstObjectByMaterial = new Dictionary<Material, GameObject>(); // To get layer/tag from representative

        // 1. Collect meshes
        foreach (var root in roots)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in filters)
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mr.sharedMaterial == null) continue;

                var mat = mr.sharedMaterial;
                if (!groupedByMaterial.ContainsKey(mat))
                {
                    groupedByMaterial[mat] = new List<CombineInstance>();
                    firstObjectByMaterial[mat] = root;
                }

                var combine = new CombineInstance();
                combine.mesh = mf.sharedMesh;
                combine.transform = mf.transform.localToWorldMatrix;
                groupedByMaterial[mat].Add(combine);
            }
        }

        // 2. Perform Combine
        foreach (var kvp in groupedByMaterial)
        {
            var material = kvp.Key;
            var combines = kvp.Value;
            var representative = firstObjectByMaterial[material];

            try 
            {
                // Create combined mesh
                var combinedMesh = new Mesh();
                // 16-bit indices are limited to 65k vertices. Switch to 32-bit if needed, or split.
                // For simplicity here, we assume standard usage, but let's toggle index format if large.
                long vertexCount = 0;
                foreach(var c in combines) vertexCount += c.mesh.vertexCount;
                
                if (vertexCount > 65000)
                    combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                combinedMesh.CombineMeshes(combines.ToArray(), true, true);
                
                // Save mesh asset
                string meshName = $"CombinedMesh_{material.name}_{Guid.NewGuid().ToString("N").Substring(0,6)}";
                string meshPath = $"{tempAssetsDir}/{meshName}.asset";
                AssetDatabase.CreateAsset(combinedMesh, meshPath);
                createdTempAssets.Add(meshPath);

                // Create combined GameObject
                var go = new GameObject($"Combined_{material.name}");
                go.layer = representative.layer; // Inherit layer (likely Default since we excluded others)
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = combinedMesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                
                // Create Prefab
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{tempAssetsDir}/{go.name}.prefab");
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                createdTempAssets.Add(prefabPath);
                
                // Instantiate to be used in export list (will be destroyed by finally block cleanup via createdTempAssets tracking? 
                // Wait, createdTempAssets deletes ASSETS on disk. 
                // formatting in export logic expects a scene root for ResolveOrCreatePrefabAssetPath... 
                // We shouldn't destroy 'go' yet if we need it for export step. 
                // Actually, ExportCurrentScene creates Temp assets, then uses them. 
                // We returned a new Prefab ASSET path. We need a Scene instance of it to pass to the rest of the pipeline?
                // The rest of the pipeline iterates 'finalExportRoots' (GameObjects in scene).
                // So we should keep 'go' in the scene.
                
                result.Add(go);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to merge objects for material {material.name}: {ex}");
            }
        }

        return result;
    }

    [MenuItem("Tools/Nexus/Export/Export Prefab Folder (.nxscene)", false, 2001)]
    public static void ExportPrefabFolder()
    {
        string folderPath = TryGetSelectedProjectFolderPath();
        if (string.IsNullOrEmpty(folderPath))
        {
            folderPath = EditorUtility.OpenFolderPanel("Select Prefab Folder", "Assets", "");
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            string dataPath = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(folderPath);
            if (!full.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Export Prefab Folder", "Selected folder must be inside this Unity project (under Assets).", "OK");
                return;
            }

            string rel = full.Substring(dataPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            folderPath = string.IsNullOrEmpty(rel) ? "Assets" : ("Assets/" + rel.Replace('\\', '/'));
        }

        string folderName = Path.GetFileName(folderPath.TrimEnd('/'));
        if (string.IsNullOrEmpty(folderName))
        {
            folderName = "Prefabs";
        }

        string defaultName = folderName + ".nxscene";
        string outputPath = EditorUtility.SaveFilePanel("Export NxScene", GetDefaultExportFolder(), defaultName, "nxscene");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "nxscene_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            var prefabPaths = prefabGuids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToArray();

            var prefabs = new List<NxScenePrefab>();
            var prefabPathByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < prefabPaths.Length; i++)
            {
                string prefabPath = prefabPaths[i];
                string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(prefabPath);
                prefabs.Add(new NxScenePrefab
                {
                    id = guid,
                    name = name,
                    bundles = new[]
                    {
                        new NxScenePlatformBundle { platform = "StandaloneWindows64", path = $"bundles/StandaloneWindows64/{GetBundleFileName(new NxScenePrefab { id = guid, name = name })}" },
                        new NxScenePlatformBundle { platform = "StandaloneOSX", path = $"bundles/StandaloneOSX/{GetBundleFileName(new NxScenePrefab { id = guid, name = name })}" },
                        new NxScenePlatformBundle { platform = "StandaloneLinux64", path = $"bundles/StandaloneLinux64/{GetBundleFileName(new NxScenePrefab { id = guid, name = name })}" },
                    }
                });

                prefabPathByGuid[guid] = prefabPath;
            }

            prefabs = prefabs.OrderBy(p => p.name).ToList();

            string bundlesRoot = Path.Combine(tempDir, "bundles");
            Directory.CreateDirectory(bundlesRoot);

            var builds = prefabs.Select(p =>
            {
                prefabPathByGuid.TryGetValue(p.id, out var path);
                return new AssetBundleBuild
                {
                    assetBundleName = GetBundleFileName(p),
                    assetNames = new[] { path }
                };
            }).Where(b => b.assetNames != null && b.assetNames.Length == 1 && !string.IsNullOrEmpty(b.assetNames[0])).ToArray();

            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneWindows64"), builds, BuildTarget.StandaloneWindows64);
            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneOSX"), builds, BuildTarget.StandaloneOSX);
            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneLinux64"), builds, BuildTarget.StandaloneLinux64);

            var manifest = new NxSceneManifest
            {
                version = "1",
                sceneName = folderName,
                prefabs = prefabs.ToArray(),
                instances = new NxSceneInstance[0]
            };

            string manifestPath = Path.Combine(tempDir, "manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest));

            CreateZip(outputPath, tempDir, manifest);
            EditorUtility.RevealInFinder(outputPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
            }
        }
    }

    private static void BuildForPlatform(string outputDir, AssetBundleBuild[] builds, BuildTarget target)
    {
        Directory.CreateDirectory(outputDir);
        BuildPipeline.BuildAssetBundles(outputDir, builds, BuildAssetBundleOptions.ChunkBasedCompression, target);
    }

    private static void CreateZip(string outputZipPath, string tempDir, NxSceneManifest manifest)
    {
        if (File.Exists(outputZipPath))
        {
            File.Delete(outputZipPath);
        }

        using var fs = new FileStream(outputZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        AddFileToZip(archive, Path.Combine(tempDir, "manifest.json"), "manifest.json");

        string bundlesDir = Path.Combine(tempDir, "bundles");
        if (Directory.Exists(bundlesDir))
        {
            foreach (var file in Directory.GetFiles(bundlesDir, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string rel = MakeRelative(tempDir, file);
                if (!rel.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    AddFileToZip(archive, file, rel.Replace('\\', '/'));
                }
            }
        }
    }

    private static void AddFileToZip(ZipArchive archive, string filePath, string entryName)
    {
        var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fileStream.CopyTo(entryStream);
    }

    private static string MakeRelative(string root, string path)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(path);
        if (p.StartsWith(r, StringComparison.OrdinalIgnoreCase))
        {
            return p.Substring(r.Length);
        }
        return path;
    }

    private static string ResolveOrCreatePrefabAssetPath(GameObject root, string tempAssetsDir, List<string> createdTempAssets)
    {
        if (root == null)
        {
            return null;
        }

        var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(root);
        bool isPrefabInstanceRoot = instanceRoot != null && instanceRoot == root && PrefabUtility.IsPartOfPrefabInstance(root);

        if (isPrefabInstanceRoot)
        {
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                bool hasOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(root, false);
                if (!hasOverrides || HasOnlyRootTransformOverrides(root))
                {
                    return prefabPath;
                }
            }
        }

        string assetName = SanitizeFileName(root.name);
        if (string.IsNullOrEmpty(assetName))
        {
            assetName = "Prefab";
        }

        string prefabAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{tempAssetsDir}/{assetName}.prefab");

        var clone = UnityEngine.Object.Instantiate(root);
        clone.name = root.name;
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        PrefabUtility.SaveAsPrefabAsset(clone, prefabAssetPath);
        UnityEngine.Object.DestroyImmediate(clone);

        createdTempAssets.Add(prefabAssetPath);
        return prefabAssetPath;
    }

    private static bool HasOnlyRootTransformOverrides(GameObject root)
    {
        if (root == null)
        {
            return false;
        }

        try
        {
            var addedComponents = PrefabUtility.GetAddedComponents(root);
            if (addedComponents != null && addedComponents.Count > 0)
            {
                return false;
            }

            var removedComponents = PrefabUtility.GetRemovedComponents(root);
            if (removedComponents != null && removedComponents.Count > 0)
            {
                return false;
            }

            var addedGameObjects = PrefabUtility.GetAddedGameObjects(root);
            if (addedGameObjects != null && addedGameObjects.Count > 0)
            {
                return false;
            }

            var removedGameObjects = PrefabUtility.GetRemovedGameObjects(root);
            if (removedGameObjects != null && removedGameObjects.Count > 0)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        var mods = PrefabUtility.GetPropertyModifications(root);
        if (mods == null || mods.Length == 0)
        {
            return true;
        }

        var t = root.transform;
        for (int i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (mod == null)
            {
                continue;
            }

            if (!ReferenceEquals(mod.target, t))
            {
                return false;
            }

            if (!IsTransformPropertyPath(mod.propertyPath))
            {
                return false;
            }
        }

        return true;
    }

    private static string TryGetSelectedProjectFolderPath()
    {
        var obj = Selection.activeObject;
        if (obj == null)
        {
            return null;
        }

        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (AssetDatabase.IsValidFolder(path))
        {
            return path;
        }

        return null;
    }

    private static bool IsTransformPropertyPath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return false;
        }

        return propertyPath.StartsWith("m_LocalPosition", StringComparison.Ordinal) ||
               propertyPath.StartsWith("m_LocalRotation", StringComparison.Ordinal) ||
               propertyPath.StartsWith("m_LocalScale", StringComparison.Ordinal) ||
               propertyPath.StartsWith("m_LocalEulerAnglesHint", StringComparison.Ordinal);
    }

    private static string GetPrefabPathByGuid(Dictionary<string, NxScenePrefab> prefabsByPath, string guid)
    {
        foreach (var kv in prefabsByPath)
        {
            if (string.Equals(kv.Value.id, guid, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Key;
            }
        }
        return null;
    }

    private static string GetBundleFileName(NxScenePrefab prefab)
    {
        string shortId = string.IsNullOrEmpty(prefab.id) ? Guid.NewGuid().ToString("N").Substring(0, 8) : prefab.id.Substring(0, 8);
        string name = SanitizeFileName(prefab.name);
        if (string.IsNullOrEmpty(name))
        {
            name = "Prefab";
        }
        return $"{name}_{shortId}.bundle".ToLowerInvariant();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c.ToString(), "_");
        }

        name = name.Replace("/", "_").Replace("\\", "_");
        return name.Trim();
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static string GetDefaultExportFolder()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs))
            {
                docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }
            if (!string.IsNullOrEmpty(docs))
            {
                return docs;
            }
        }
        catch
        {
        }

        return Application.dataPath;
    }
}
#endif
