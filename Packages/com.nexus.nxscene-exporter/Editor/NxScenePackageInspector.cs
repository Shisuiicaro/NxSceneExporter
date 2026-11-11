#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Nexus.NxScene;

namespace Nexus.NxScene.Editor
{
    public sealed class NxScenePackageInspector : EditorWindow
    {
        private string m_packagePath;
        private string m_report = string.Empty;
        private string m_lastReportPath;
        private Vector2 m_scroll;

        [MenuItem("Tools/Nexus/Inspect Scene Package", false, 2002)]
        private static void Open()
        {
            var window = GetWindow<NxScenePackageInspector>();
            window.titleContent = new GUIContent("NxScene Inspector");
            window.minSize = new Vector2(640, 520);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Package", GUILayout.Width(60));
            m_packagePath = EditorGUILayout.TextField(m_packagePath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                var selected = EditorUtility.OpenFilePanel("Inspect Scene Package", string.Empty, "nxscene");
                if (!string.IsNullOrEmpty(selected)) m_packagePath = selected;
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_packagePath) || !File.Exists(m_packagePath)))
            {
                if (GUILayout.Button("Inspect", GUILayout.Width(80))) Inspect();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_report)))
            {
                if (GUILayout.Button("Copy")) EditorGUIUtility.systemCopyBuffer = m_report;
                if (GUILayout.Button("Save report")) SaveReport();
            }
            if (!string.IsNullOrEmpty(m_lastReportPath) && File.Exists(m_lastReportPath) && GUILayout.Button("Reveal report"))
                EditorUtility.RevealInFinder(m_lastReportPath);
            EditorGUILayout.EndHorizontal();

            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            m_report = EditorGUILayout.TextArea(m_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Inspect()
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine("NxScene package: " + Path.GetFileName(m_packagePath));
                using (var stream = File.OpenRead(m_packagePath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var manifestEntry = archive.GetEntry("manifest.json") ?? archive.Entries.FirstOrDefault(entry => entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
                    if (manifestEntry == null) throw new InvalidDataException("manifest.json was not found in the package");
                    var manifest = JsonUtility.FromJson<NxSceneManifest>(ReadText(manifestEntry));
                    if (manifest == null) throw new InvalidDataException("manifest.json could not be parsed");
                    report.AppendLine("Scene: " + manifest.sceneName);
                    report.AppendLine("Package: " + manifest.packageName + " " + manifest.packageVersion);
                    report.AppendLine("Prefabs: " + (manifest.prefabs != null ? manifest.prefabs.Length : 0) + "; instances: " + (manifest.instances != null ? manifest.instances.Length : 0) + "; LOD groups: " + (manifest.lodGroups != null ? manifest.lodGroups.Length : 0));
                    AppendDiagnostics(report, manifest.diagnostics);
                    var bundlePath = ExtractBundle(archive, manifest);
                    if (bundlePath == null)
                    {
                        report.AppendLine("No platform bundle was found.");
                    }
                    else
                    {
                        var bundle = AssetBundle.LoadFromFile(bundlePath);
                        if (bundle == null)
                        {
                            report.AppendLine("Bundle could not be loaded: " + bundlePath);
                        }
                        else
                        {
                            try { AppendBundleReport(report, bundle); }
                            finally { bundle.Unload(true); }
                        }
                        try { File.Delete(bundlePath); } catch { }
                    }
                }
                m_report = report.ToString();
                m_lastReportPath = Path.ChangeExtension(m_packagePath, ".diagnostics.txt");
                try { File.WriteAllText(m_lastReportPath, m_report); } catch { }
                Debug.Log("[NxSceneInspector] inspected " + Path.GetFileName(m_packagePath) + "; report=" + m_lastReportPath);
            }
            catch (Exception exception)
            {
                m_report = "Inspection failed: " + exception.Message + Environment.NewLine + exception;
                Debug.LogException(exception);
            }
            Repaint();
        }

        private void SaveReport()
        {
            var path = EditorUtility.SaveFilePanel("Save diagnostics report", Path.GetDirectoryName(m_packagePath) ?? string.Empty, Path.GetFileNameWithoutExtension(m_packagePath) + ".diagnostics.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, m_report);
            m_lastReportPath = path;
            EditorUtility.RevealInFinder(path);
        }

        private static string ReadText(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        private static string ExtractBundle(ZipArchive archive, NxSceneManifest manifest)
        {
            if (manifest.prefabs == null || manifest.prefabs.Length == 0) return null;
            var platform = Application.platform == RuntimePlatform.WindowsEditor ? "StandaloneWindows64" :
                Application.platform == RuntimePlatform.LinuxEditor ? "StandaloneLinux64" :
                Application.platform == RuntimePlatform.OSXEditor ? "StandaloneOSX" : "StandaloneWindows64";
            var bundle = manifest.prefabs.Where(prefab => prefab != null && prefab.bundles != null)
                .SelectMany(prefab => prefab.bundles)
                .FirstOrDefault(candidate => candidate != null && candidate.platform == platform)
                ?? manifest.prefabs.Where(prefab => prefab != null && prefab.bundles != null)
                .SelectMany(prefab => prefab.bundles)
                .FirstOrDefault(candidate => candidate != null);
            if (bundle == null || string.IsNullOrWhiteSpace(bundle.path)) return null;
            var entry = archive.GetEntry(bundle.path.Replace('\\', '/')) ?? archive.Entries.FirstOrDefault(candidate => candidate.FullName.Equals(bundle.path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            var path = Path.Combine(Path.GetTempPath(), "nxscene_inspect_" + Guid.NewGuid().ToString("N") + ".bundle");
            entry.ExtractToFile(path, true);
            return path;
        }

        private static void AppendDiagnostics(StringBuilder report, NxSceneDiagnostics diagnostics)
        {
            report.AppendLine();
            if (diagnostics == null)
            {
                report.AppendLine("Source diagnostics: not recorded in this package.");
                return;
            }
            report.AppendLine("Source diagnostics:");
            report.AppendLine("  Exporter: " + diagnostics.exporterVersion);
            report.AppendLine("  VisualEffect components: " + diagnostics.visualEffectCount);
            report.AppendLine("  Particle systems: " + diagnostics.particleSystemCount);
            report.AppendLine("  Removed components: " + diagnostics.removedComponentCount);
            if (diagnostics.removedComponents != null)
                foreach (var removed in diagnostics.removedComponents)
                    report.AppendLine("  Removed " + removed.component + " at " + removed.path);
            if (diagnostics.warnings != null)
                foreach (var warning in diagnostics.warnings) report.AppendLine("  Warning: " + warning);
            if (diagnostics.prefabs != null)
                foreach (var prefab in diagnostics.prefabs)
                {
                    if (prefab == null || (prefab.visualEffectCount == 0 && (prefab.removedComponents == null || prefab.removedComponents.Length == 0))) continue;
                    report.AppendLine("  Prefab " + prefab.name + ":");
                    report.AppendLine("    source: " + Join(prefab.sourceComponents));
                    report.AppendLine("    exported: " + Join(prefab.exportedComponents));
                    report.AppendLine("    removed: " + Join(prefab.removedComponents));
                }
        }

        private static void AppendBundleReport(StringBuilder report, AssetBundle bundle)
        {
            var prefabs = bundle.LoadAllAssets<GameObject>();
            report.AppendLine();
            report.AppendLine("Bundle prefabs: " + prefabs.Length);
            int particlePrefabs = 0;
            int visualEffects = 0;
            int monoBehaviours = 0;
            int emptyPrefabs = 0;
            foreach (var prefab in prefabs.OrderBy(candidate => candidate.name, StringComparer.OrdinalIgnoreCase))
            {
                var components = prefab.GetComponents<Component>().Where(component => component != null).ToArray();
                var allComponents = prefab.GetComponentsInChildren<Component>(true).Where(component => component != null).ToArray();
                var particles = allComponents.OfType<ParticleSystem>().ToArray();
                var effects = allComponents.Count(component => component.GetType().FullName == "UnityEngine.VFX.VisualEffect");
                var volumes = allComponents.Where(component => component.GetType().FullName == "UnityEngine.Rendering.Volume").ToArray();
                var monos = allComponents.Count(component => component is MonoBehaviour);
                if (particles.Length > 0) particlePrefabs++;
                visualEffects += effects;
                monoBehaviours += monos;
                if (components.Length == 1 && components[0] is Transform) emptyPrefabs++;
                if (particles.Length == 0 && effects == 0 && volumes.Length == 0) continue;
                report.AppendLine("- " + prefab.name + ": " + string.Join(", ", components.Select(component => component.GetType().Name).ToArray()));
                foreach (var volume in volumes)
                {
                    var property = volume.GetType().GetProperty("sharedProfile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    var profile = property?.GetValue(volume, null) as UnityEngine.Object;
                    report.AppendLine("  Volume " + volume.name + ": profile=" + (profile != null ? profile.name : "missing"));
                }
                foreach (var particle in particles)
                {
                    report.AppendLine("  ParticleSystem " + particle.name + ": startColor=" + particle.main.startColor.color + "; emission=" + particle.emission.rateOverTime.constantMin + ".." + particle.emission.rateOverTime.constantMax + "; customData=" + particle.customData.enabled + "; textureSheet=" + particle.textureSheetAnimation.enabled);
                    var renderer = particle.GetComponent<ParticleSystemRenderer>();
                    if (renderer == null) continue;
                    report.AppendLine("  Renderer " + renderer.name + ": mode=" + renderer.renderMode + "; customStreams=" + GetUseCustomVertexStreams(renderer) + " [" + string.Join(",", GetVertexStreams(renderer).Select(stream => stream.ToString()).ToArray()) + "]");
                    foreach (var material in (renderer.sharedMaterials ?? Array.Empty<Material>()).Where(material => material != null))
                    {
                        report.AppendLine("    Material " + material.name + ": shader=" + (material.shader != null ? material.shader.name : "missing") + "; supported=" + (material.shader != null && material.shader.isSupported) + "; queue=" + material.renderQueue + "; keywords=" + Join(material.shaderKeywords));
                        if (material.HasProperty("_MainTex"))
                        {
                            var texture = material.GetTexture("_MainTex");
                            report.AppendLine("      _MainTex=" + (texture != null ? texture.name + (texture is Texture2D texture2D ? " " + texture2D.width + "x" + texture2D.height : string.Empty) : "missing"));
                        }
                        if (material.HasProperty("_Color")) report.AppendLine("      _Color=" + material.GetColor("_Color"));
                        if (material.HasProperty("_FinalEmission")) report.AppendLine("      _FinalEmission=" + material.GetFloat("_FinalEmission"));
                    }
                }
            }
            report.AppendLine("Summary: MonoBehaviour=" + monoBehaviours + "; VisualEffect=" + visualEffects + "; particlePrefabs=" + particlePrefabs + "; emptyPrefabs=" + emptyPrefabs);
            if (visualEffects == 0 && monoBehaviours == 0) report.AppendLine("No VFX Graph components were exported. Any source VisualEffect component was removed.");
            var materials = bundle.LoadAllAssets<Material>();
            var shaders = bundle.LoadAllAssets<Shader>();
            report.AppendLine("Bundle materials: " + materials.Length + "; shaders: " + shaders.Length);
            foreach (var material in materials.Where(material => material != null && material.shader != null && (!material.shader.isSupported || material.shader.name.StartsWith("Shader Graphs/", StringComparison.Ordinal))).OrderBy(material => material.name, StringComparer.OrdinalIgnoreCase))
                report.AppendLine("- Material " + material.name + ": shader=" + material.shader.name + "; supported=" + material.shader.isSupported + "; queue=" + material.renderQueue + "; keywords=" + Join(material.shaderKeywords));
        }

        private static string Join(string[] values)
        {
            return values == null || values.Length == 0 ? "none" : string.Join(", ", values);
        }

        private static bool GetUseCustomVertexStreams(ParticleSystemRenderer renderer)
        {
            var serialized = new SerializedObject(renderer);
            var property = serialized.FindProperty("m_UseCustomVertexStreams");
            return property != null && property.boolValue;
        }

        private static int[] GetVertexStreams(ParticleSystemRenderer renderer)
        {
            var serialized = new SerializedObject(renderer);
            var property = serialized.FindProperty("m_VertexStreams");
            if (property == null || !property.isArray) return Array.Empty<int>();
            var streams = new int[property.arraySize];
            for (int i = 0; i < streams.Length; i++) streams[i] = property.GetArrayElementAtIndex(i).intValue;
            return streams;
        }
    }
}
#endif
