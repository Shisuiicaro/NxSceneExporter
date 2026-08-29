using System;
using UnityEngine;

namespace Nexus.NxScene
{
    [Serializable]
    public class NxSceneManifest
    {
        public string version;
        public string sceneName;
        public NxScenePrefab[] prefabs;
        public NxSceneInstance[] instances;
    }

    [Serializable]
    public class NxScenePrefab
    {
        public string id;
        public string name;
        public NxScenePlatformBundle[] bundles;
    }

    [Serializable]
    public class NxScenePlatformBundle
    {
        public string platform;
        public string path;
    }

    [Serializable]
    public class NxSceneInstance
    {
        public string name;
        public string layer;
        public string prefabId;
        public int parentIndex;
        public string parentPath;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }
}
