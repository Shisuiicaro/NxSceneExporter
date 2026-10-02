using System;
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
}
