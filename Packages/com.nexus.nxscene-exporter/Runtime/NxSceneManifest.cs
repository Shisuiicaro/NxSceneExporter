using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nexus.NxScene
{
    [Serializable]
    public sealed class NxSceneManifest
    {
        public string version = "2";
        public string sceneName;
        public string packageId;
        public string packageName;
        public string packageVersion;
        public string author;
        public string[] platforms;
        public NxScenePrefab[] prefabs;
        public NxSceneInstance[] instances;
        public NxSceneLodGroup[] lodGroups;
        public NxSceneFileEntry[] files;
    }

    [Serializable]
    public sealed class NxScenePrefab
    {
        public string id;
        public string name;
        public bool isFoliage;
        public NxScenePlatformBundle[] bundles;
    }

    [Serializable]
    public sealed class NxScenePlatformBundle
    {
        public string platform;
        public string path;
        public string asset;
    }

    [Serializable]
    public sealed class NxSceneInstance
    {
        public string name;
        public string layer;
        public string prefabId;
        public int parentIndex = -1;
        public string parentPath;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale = Vector3.one;
    }

    [Serializable]
    public sealed class NxSceneFileEntry
    {
        public string path;
        public string sha256;
        public long size;
    }

    [Serializable]
    public sealed class NxSceneLodGroup
    {
        public int instanceIndex;
        public int componentIndex;
        public bool enabled;
        public int fadeMode;
        public bool animateCrossFading;
        public Vector3 localReferencePoint;
        public float size;
        public NxSceneLodLevel[] levels;
    }

    [Serializable]
    public sealed class NxSceneLodLevel
    {
        public float transitionHeight;
        public float fadeTransitionWidth;
        public NxSceneLodRenderer[] renderers;
    }

    [Serializable]
    public sealed class NxSceneLodRenderer
    {
        public int instanceIndex;
        public int componentIndex;
    }

    public static class NxSceneLodOwnership
    {
        public static int NormalizeNestedRendererOwnership(NxSceneLodGroup[] groups, NxSceneInstance[] instances)
        {
            if (groups == null || groups.Length == 0) return 0;
            if (instances == null) throw new InvalidDataException("LOD group data has no scene instances");
            var rendererOwners = new Dictionary<(int Instance, int Component), HashSet<int>>();
            foreach (var group in groups)
            {
                if (group?.levels == null) continue;
                foreach (var level in group.levels)
                {
                    if (level?.renderers == null) continue;
                    foreach (var renderer in level.renderers)
                    {
                        if (renderer == null) continue;
                        var key = (renderer.instanceIndex, renderer.componentIndex);
                        if (!rendererOwners.TryGetValue(key, out var owners)) rendererOwners.Add(key, owners = new HashSet<int>());
                        owners.Add(group.instanceIndex);
                    }
                }
            }
            int normalized = 0;
            foreach (var entry in rendererOwners)
            {
                if (entry.Value.Count < 2) continue;
                int deepestOwner = -1;
                foreach (int candidate in entry.Value)
                {
                    bool deepest = true;
                    foreach (int other in entry.Value)
                    {
                        if (!IsInstanceAncestor(other, candidate, instances))
                        {
                            deepest = false;
                            break;
                        }
                    }
                    if (!deepest) continue;
                    if (deepestOwner >= 0) throw new InvalidDataException("LOD renderer belongs to more than one group");
                    deepestOwner = candidate;
                }
                if (deepestOwner < 0) throw new InvalidDataException("LOD renderer belongs to more than one group");
                foreach (var group in groups)
                {
                    if (group == null || group.instanceIndex == deepestOwner) continue;
                    foreach (var level in group.levels)
                    {
                        int before = level.renderers.Length;
                        level.renderers = Array.FindAll(level.renderers, renderer => renderer.instanceIndex != entry.Key.Instance || renderer.componentIndex != entry.Key.Component);
                        normalized += before - level.renderers.Length;
                    }
                }
            }
            return normalized;
        }

        private static bool IsInstanceAncestor(int ancestor, int instance, NxSceneInstance[] instances)
        {
            var visited = new HashSet<int>();
            int current = instance;
            while (current >= 0 && current < instances.Length && visited.Add(current))
            {
                if (current == ancestor) return true;
                if (instances[current] == null) return false;
                current = instances[current].parentIndex;
            }
            return false;
        }
    }
}
