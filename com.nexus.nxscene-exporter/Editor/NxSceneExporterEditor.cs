#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class NxSceneExporterWithLayersEditor
{
    [MenuItem("Tools/Nexus/Export/Export Current Scene (.nxscene + Layers)", false, 2000)]
    public static void ExportCurrentScene()
    {
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

        try
        {
            var exportRoots = scene.GetRootGameObjects()
                .Where(go => go != null)
                .Where(go => go.hideFlags == HideFlags.None)
                .ToArray();

            var prefabPathByRoot = new Dictionary<GameObject, string>();
            foreach (var root in exportRoots)
            {
                string prefabPath = ResolveOrCreatePrefabAssetPath(root, tempAssetsDir, createdTempAssets);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    continue;
                }
                prefabPathByRoot[root] = prefabPath;
            }

            var prefabEntriesByPath = new Dictionary<string, NxScenePrefabData>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in prefabPathByRoot)
            {
                string prefabPath = kv.Value;
                if (prefabEntriesByPath.ContainsKey(prefabPath))
                {
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                string name = Path.GetFileNameWithoutExtension(prefabPath);

                prefabEntriesByPath[prefabPath] = new NxScenePrefabData
                {
                    id = guid,
                    name = name,
                    bundles = new NxScenePlatformBundleData[0]
                };
            }

            var prefabs = prefabEntriesByPath.Values.OrderBy(p => p.name).ToArray();
            var instances = new List<NxSceneInstanceData>();

            foreach (var root in exportRoots)
            {
                if (!prefabPathByRoot.TryGetValue(root, out var prefabPath))
                {
                    continue;
                }

                var prefab = prefabEntriesByPath[prefabPath];

                instances.Add(new NxSceneInstanceData
                {
                    name = root.name,
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

            var manifestPrefabs = new List<NxScenePrefabData>();
            foreach (var p in prefabs)
            {
                manifestPrefabs.Add(new NxScenePrefabData
                {
                    id = p.id,
                    name = p.name,
                    bundles = new[]
                    {
                        new NxScenePlatformBundleData { platform = "StandaloneWindows64", path = $"bundles/StandaloneWindows64/{GetBundleFileName(p)}" },
                        new NxScenePlatformBundleData { platform = "StandaloneOSX", path = $"bundles/StandaloneOSX/{GetBundleFileName(p)}" },
                        new NxScenePlatformBundleData { platform = "StandaloneLinux64", path = $"bundles/StandaloneLinux64/{GetBundleFileName(p)}" },
                    }
                });
            }

            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneWindows64"), builds, BuildTarget.StandaloneWindows64);
            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneOSX"), builds, BuildTarget.StandaloneOSX);
            BuildForPlatform(Path.Combine(bundlesRoot, "StandaloneLinux64"), builds, BuildTarget.StandaloneLinux64);

            var manifest = new NxSceneManifestData
            {
                version = "2",
                sceneName = sceneName,
                layerNames = CaptureLayerNames(),
                prefabs = manifestPrefabs.ToArray(),
                instances = instances.ToArray()
            };

            string manifestPath = Path.Combine(tempDir, "manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest));

            CreateZip(outputPath, tempDir);

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

    [MenuItem("Tools/Nexus/Export/Export Prefab Folder (.nxscene + Layers)", false, 2001)]
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

            var prefabs = new List<NxScenePrefabData>();
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
                prefabs.Add(new NxScenePrefabData
                {
                    id = guid,
                    name = name,
                    bundles = new[]
                    {
                        new NxScenePlatformBundleData { platform = "StandaloneWindows64", path = $"bundles/StandaloneWindows64/{GetBundleFileName(new NxScenePrefabData { id = guid, name = name })}" },
                        new NxScenePlatformBundleData { platform = "StandaloneOSX", path = $"bundles/StandaloneOSX/{GetBundleFileName(new NxScenePrefabData { id = guid, name = name })}" },
                        new NxScenePlatformBundleData { platform = "StandaloneLinux64", path = $"bundles/StandaloneLinux64/{GetBundleFileName(new NxScenePrefabData { id = guid, name = name })}" },
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

            var manifest = new NxSceneManifestData
            {
                version = "2",
                sceneName = folderName,
                layerNames = CaptureLayerNames(),
                prefabs = prefabs.ToArray(),
                instances = new NxSceneInstanceData[0]
            };

            string manifestPath = Path.Combine(tempDir, "manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest));

            CreateZip(outputPath, tempDir);
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

    private static string[] CaptureLayerNames()
    {
        var names = new string[32];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = LayerMask.LayerToName(i);
        }
        return names;
    }

    private static void BuildForPlatform(string outputDir, AssetBundleBuild[] builds, BuildTarget target)
    {
        Directory.CreateDirectory(outputDir);
        BuildPipeline.BuildAssetBundles(outputDir, builds, BuildAssetBundleOptions.ChunkBasedCompression, target);
    }

    private static void CreateZip(string outputZipPath, string tempDir)
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
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
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

    private static string GetPrefabPathByGuid(Dictionary<string, NxScenePrefabData> prefabsByPath, string guid)
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

    private static string GetBundleFileName(NxScenePrefabData prefab)
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

    [Serializable]
    private class NxSceneManifestData
    {
        public string version;
        public string sceneName;
        public string[] layerNames;
        public NxScenePrefabData[] prefabs;
        public NxSceneInstanceData[] instances;
    }

    [Serializable]
    private class NxScenePrefabData
    {
        public string id;
        public string name;
        public NxScenePlatformBundleData[] bundles;
    }

    [Serializable]
    private class NxScenePlatformBundleData
    {
        public string platform;
        public string path;
    }

    [Serializable]
    private class NxSceneInstanceData
    {
        public string name;
        public string prefabId;
        public int parentIndex;
        public string parentPath;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }
}
#endif
