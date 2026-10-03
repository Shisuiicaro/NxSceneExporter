#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Nexus.NxScene;

namespace Nexus.NxScene.Editor
{
    public sealed class NxSceneExporterWindow : EditorWindow
    {
        private enum SourceKind
        {
            CurrentScene,
            PrefabFolder
        }

        private sealed class ExportItem
        {
            public GameObject source;
            public string assetPath;
            public string id;
            public string name;
        }

        private sealed class ModelReadabilityState
        {
            public string path;
            public bool wasReadable;
        }

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "title", "Nexus Scene Exporter" },
            { "subtitle", "Create a portable scene package for Nexus" },
            { "source", "Source" },
            { "currentScene", "Current scene" },
            { "prefabFolder", "Prefab folder" },
            { "folder", "Folder" },
            { "chooseFolder", "Choose folder" },
            { "identity", "Package identity" },
            { "packageId", "Package ID" },
            { "packageName", "Package name" },
            { "version", "Version" },
            { "author", "Author" },
            { "options", "Export options" },
            { "targets", "Targets" },
            { "windows", "Windows" },
            { "linux", "Linux" },
            { "thumbnail", "Capture thumbnail" },
            { "output", "Output" },
            { "outputFolder", "Output folder" },
            { "chooseOutput", "Choose output" },
            { "preflight", "Run preflight" },
            { "export", "Export package" },
            { "ready", "Ready to export" },
            { "errors", "Export blocked" },
            { "warnings", "Warnings" },
            { "scriptsRemoved", "Scripts will be removed from the export" },
            { "noErrors", "No blocking issues found" },
            { "selectOutput", "Choose an output folder" },
            { "sceneUnsaved", "Save the current scene first" },
            { "folderMissing", "Choose a prefab folder" },
            { "noPrefabs", "No prefabs found in the folder" },
            { "urpRequired", "A URP render pipeline is required" },
            { "compiling", "Wait for Unity to finish compiling" },
            { "scriptCompilationFailed", "Project scripts contain errors" },
            { "windowsSupportMissing", "Windows export support is missing" },
            { "linuxSupportMissing", "Linux export support is missing" },
            { "bundleBuildFailed", "The {0} bundle could not be created" },
            { "idRequired", "Package ID is required" },
            { "idInvalid", "Package ID uses letters, numbers, dots, and hyphens" },
            { "nameRequired", "Package name is required" },
            { "versionRequired", "Package version is required" },
            { "exporting", "Exporting package" },
            { "building", "Building {0} bundles" },
            { "success", "Package exported" },
            { "packageMissing", "Package file was not created" },
            { "failed", "Export failed" },
            { "openFolder", "Open folder" },
            { "ok", "OK" },
            { "cancel", "Cancel" },
            { "sceneName", "Scene" },
            { "packageDefault", "Nexus package" },
            { "defaultAuthor", "Nexus creator" },
            { "lodRefsInvalid", "An LOD group references an object outside the scene" }
        };

        private static readonly Dictionary<string, string> Portuguese = new Dictionary<string, string>
        {
            { "title", "Exportador de Cenas Nexus" },
            { "subtitle", "Crie um pacote de cena para o Nexus" },
            { "source", "Origem" },
            { "currentScene", "Cena atual" },
            { "prefabFolder", "Pasta de prefabs" },
            { "folder", "Pasta" },
            { "chooseFolder", "Escolher pasta" },
            { "identity", "Identidade do pacote" },
            { "packageId", "ID do pacote" },
            { "packageName", "Nome do pacote" },
            { "version", "Versão" },
            { "author", "Autor" },
            { "options", "Opções de exportação" },
            { "targets", "Destinos" },
            { "windows", "Windows" },
            { "linux", "Linux" },
            { "thumbnail", "Capturar miniatura" },
            { "output", "Saída" },
            { "outputFolder", "Pasta de saída" },
            { "chooseOutput", "Escolher saída" },
            { "preflight", "Verificar pacote" },
            { "export", "Exportar pacote" },
            { "ready", "Pronto para exportar" },
            { "errors", "Exportação bloqueada" },
            { "warnings", "Avisos" },
            { "scriptsRemoved", "Os scripts serão removidos da exportação" },
            { "noErrors", "Nenhum problema bloqueante" },
            { "selectOutput", "Escolha uma pasta de saída" },
            { "sceneUnsaved", "Salve a cena atual primeiro" },
            { "folderMissing", "Escolha uma pasta de prefabs" },
            { "noPrefabs", "Nenhum prefab encontrado na pasta" },
            { "urpRequired", "Um pipeline URP é necessário" },
            { "compiling", "Aguarde a compilação do Unity" },
            { "scriptCompilationFailed", "Os scripts do projeto têm erros" },
            { "windowsSupportMissing", "O suporte de exportação para Windows está ausente" },
            { "linuxSupportMissing", "O suporte de exportação para Linux está ausente" },
            { "bundleBuildFailed", "Não foi possível criar o bundle {0}" },
            { "idRequired", "O ID do pacote é necessário" },
            { "idInvalid", "Use letras, números, pontos e hífens no ID" },
            { "nameRequired", "O nome do pacote é necessário" },
            { "versionRequired", "A versão do pacote é necessária" },
            { "exporting", "Exportando pacote" },
            { "building", "Criando bundles {0}" },
            { "success", "Pacote exportado" },
            { "packageMissing", "O arquivo do pacote não foi criado" },
            { "failed", "Falha na exportação" },
            { "openFolder", "Abrir pasta" },
            { "ok", "OK" },
            { "cancel", "Cancelar" },
            { "sceneName", "Cena" },
            { "packageDefault", "Pacote Nexus" },
            { "defaultAuthor", "Criador Nexus" },
            { "lodRefsInvalid", "Um grupo LOD referencia um objeto fora da cena" }
        };

        private SourceKind m_source;
        private string m_prefabFolder = "Assets";
        private string m_outputFolder;
        private string m_packageId;
        private string m_packageName;
        private string m_packageVersion = "1.0.0";
        private string m_author;
        private bool m_thumbnail = true;
        private Vector2 m_scroll;
        private readonly List<string> m_errors = new List<string>();
        private readonly List<string> m_warnings = new List<string>();

        [MenuItem("Tools/Nexus/Export Scene Package", false, 2000)]
        private static void OpenCurrentScene()
        {
            Open(SourceKind.CurrentScene);
        }

        [MenuItem("Tools/Nexus/Export Prefab Folder", false, 2001)]
        private static void OpenPrefabFolder()
        {
            Open(SourceKind.PrefabFolder);
        }

        private static void Open(SourceKind source)
        {
            var window = GetWindow<NxSceneExporterWindow>();
            window.titleContent = new GUIContent(window.T("title"));
            window.m_source = source;
            window.InitializeDefaults();
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(560, 620);
            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            var scene = SceneManager.GetActiveScene();
            var sceneName = string.IsNullOrWhiteSpace(scene.name) ? T("packageDefault") : scene.name;
            if (string.IsNullOrWhiteSpace(m_packageName)) m_packageName = sceneName;
            if (string.IsNullOrWhiteSpace(m_packageId)) m_packageId = Slug(sceneName);
            if (string.IsNullOrWhiteSpace(m_author)) m_author = T("defaultAuthor");
            if (string.IsNullOrWhiteSpace(m_outputFolder)) m_outputFolder = GetDefaultOutputFolder();
        }

        private void OnGUI()
        {
            DrawHeader();
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            DrawSource();
            DrawIdentity();
            DrawOptions();
            DrawOutput();
            DrawValidation();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            var rect = EditorGUILayout.GetControlRect(false, 72);
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.12f, 0.18f));
            GUI.Label(new Rect(rect.x + 18, rect.y + 14, rect.width - 36, 28), T("title"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, normal = { textColor = Color.white } });
            GUI.Label(new Rect(rect.x + 19, rect.y + 43, rect.width - 38, 18), T("subtitle"), new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.65f, 0.75f, 0.86f) } });
            GUILayout.Space(8);
        }

        private void DrawSource()
        {
            BeginCard(T("source"));
            m_source = (SourceKind)GUILayout.Toolbar((int)m_source, new[] { T("currentScene"), T("prefabFolder") });
            if (m_source == SourceKind.PrefabFolder)
            {
                EditorGUILayout.BeginHorizontal();
                m_prefabFolder = EditorGUILayout.TextField(T("folder"), m_prefabFolder);
                if (GUILayout.Button(T("chooseFolder"), GUILayout.Width(120)))
                {
                    var selected = EditorUtility.OpenFolderPanel(T("chooseFolder"), Application.dataPath, string.Empty);
                    if (!string.IsNullOrEmpty(selected)) m_prefabFolder = ToAssetPath(selected);
                }
                EditorGUILayout.EndHorizontal();
            }
            EndCard();
        }

        private void DrawIdentity()
        {
            BeginCard(T("identity"));
            m_packageId = EditorGUILayout.TextField(T("packageId"), m_packageId);
            m_packageName = EditorGUILayout.TextField(T("packageName"), m_packageName);
            m_packageVersion = EditorGUILayout.TextField(T("version"), m_packageVersion);
            m_author = EditorGUILayout.TextField(T("author"), m_author);
            EndCard();
        }

        private void DrawOptions()
        {
            BeginCard(T("options"));
            EditorGUILayout.LabelField(T("targets"), T("windows") + ", " + T("linux"));
            m_thumbnail = EditorGUILayout.ToggleLeft(T("thumbnail"), m_thumbnail);
            EndCard();
        }

        private void DrawOutput()
        {
            BeginCard(T("output"));
            EditorGUILayout.BeginHorizontal();
            m_outputFolder = EditorGUILayout.TextField(T("outputFolder"), m_outputFolder);
            if (GUILayout.Button(T("chooseOutput"), GUILayout.Width(120)))
            {
                var selected = EditorUtility.OpenFolderPanel(T("chooseOutput"), m_outputFolder, string.Empty);
                if (!string.IsNullOrEmpty(selected)) m_outputFolder = selected;
            }
            EditorGUILayout.EndHorizontal();
            EndCard();
        }

        private void DrawValidation()
        {
            BeginCard(T("preflight"));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("preflight"), GUILayout.Height(30))) Validate();
            using (new EditorGUI.DisabledScope(m_errors.Count > 0))
            {
                if (GUILayout.Button(T("export"), GUILayout.Height(30))) Export();
            }
            EditorGUILayout.EndHorizontal();
            if (m_errors.Count == 0 && m_warnings.Count == 0) EditorGUILayout.HelpBox(T("noErrors"), MessageType.Info);
            if (m_errors.Count > 0)
            {
                EditorGUILayout.LabelField(T("errors"), EditorStyles.boldLabel);
                foreach (var error in m_errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            foreach (var warning in m_warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            EndCard();
        }

        private void Validate()
        {
            m_errors.Clear();
            m_warnings.Clear();
            if (string.IsNullOrWhiteSpace(m_outputFolder) || !Directory.Exists(m_outputFolder)) m_errors.Add(T("selectOutput"));
            if (string.IsNullOrWhiteSpace(m_packageId)) m_errors.Add(T("idRequired"));
            else if (!System.Text.RegularExpressions.Regex.IsMatch(m_packageId, "^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$")) m_errors.Add(T("idInvalid"));
            if (string.IsNullOrWhiteSpace(m_packageName)) m_errors.Add(T("nameRequired"));
            if (string.IsNullOrWhiteSpace(m_packageVersion)) m_errors.Add(T("versionRequired"));
            if (GraphicsSettings.renderPipelineAsset == null || !GraphicsSettings.renderPipelineAsset.GetType().Name.Contains("Universal")) m_errors.Add(T("urpRequired"));
            if (EditorApplication.isCompiling) m_errors.Add(T("compiling"));
            else if (EditorUtility.scriptCompilationFailed) m_errors.Add(T("scriptCompilationFailed"));
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64)) m_errors.Add(T("windowsSupportMissing"));
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64)) m_errors.Add(T("linuxSupportMissing"));
            if (m_source == SourceKind.CurrentScene && string.IsNullOrWhiteSpace(SceneManager.GetActiveScene().path)) m_errors.Add(T("sceneUnsaved"));
            var roots = GetRoots();
            if (roots.Count == 0) m_errors.Add(m_source == SourceKind.CurrentScene ? T("sceneUnsaved") : T("noPrefabs"));
            try { BuildLodGroups(roots.Select(root => new ExportItem { source = root }).ToList()); }
            catch { m_errors.Add(T("lodRefsInvalid")); }
            if (roots.Any(RequiresScriptStrip)) m_warnings.Add(T("scriptsRemoved"));
            Repaint();
        }

        private void Export()
        {
            Validate();
            if (m_errors.Count > 0)
            {
                EditorUtility.DisplayDialog(T("errors"), string.Join("\n", m_errors), T("ok"));
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "nxscene_" + Guid.NewGuid().ToString("N"));
            string tempAssetRoot = "Assets/Nexus/Temp/NxSceneExport/" + Guid.NewGuid().ToString("N");
            var createdAssets = new List<string>();
            var items = new List<ExportItem>();
            var assetItems = new List<ExportItem>();
            var assetBySignature = new Dictionary<string, ExportItem>(StringComparer.Ordinal);
            bool assetEditing = false;
            try
            {
                Directory.CreateDirectory(tempDir);
                var roots = GetRoots();
                AssetDatabase.StartAssetEditing();
                assetEditing = true;
                EnsureAssetFolder("Assets/Nexus/Temp");
                EnsureAssetFolder("Assets/Nexus/Temp/NxSceneExport");
                EnsureAssetFolder(tempAssetRoot);
                for (int i = 0; i < roots.Count; i++)
                {
                    EditorUtility.DisplayProgressBar(T("exporting"), roots[i].name, (float)i / Math.Max(1, roots.Count));
                    string sourcePath = AssetDatabase.GetAssetPath(roots[i]);
                    string signature = m_source == SourceKind.PrefabFolder && !string.IsNullOrEmpty(sourcePath) ? "asset:" + AssetDatabase.AssetPathToGUID(sourcePath) : BuildAssetSignature(roots[i]);
                    if (!assetBySignature.TryGetValue(signature, out var assetItem))
                    {
                        string assetPath = sourcePath;
                        if (m_source == SourceKind.CurrentScene || string.IsNullOrEmpty(sourcePath) || RequiresScriptStrip(roots[i]))
                        {
                            var clone = m_source == SourceKind.CurrentScene ? CreateSingleObjectClone(roots[i]) : Instantiate(roots[i]);
                            clone.name = roots[i].name;
                            clone.transform.localPosition = Vector3.zero;
                            clone.transform.localRotation = Quaternion.identity;
                            clone.transform.localScale = Vector3.one;
                            if (m_source != SourceKind.CurrentScene) StripScripts(clone);
                            assetPath = AssetDatabase.GenerateUniqueAssetPath(tempAssetRoot + "/" + Sanitize(roots[i].name) + ".prefab");
                            PrefabUtility.SaveAsPrefabAsset(clone, assetPath);
                            DestroyImmediate(clone);
                            createdAssets.Add(assetPath);
                        }
                        string id = m_source == SourceKind.CurrentScene ? StableId(string.Empty, signature, 0) : StableId(sourcePath, roots[i].name, i);
                        assetItem = new ExportItem { source = roots[i], assetPath = assetPath, id = id, name = roots[i].name };
                        assetBySignature.Add(signature, assetItem);
                        assetItems.Add(assetItem);
                    }
                    items.Add(new ExportItem { source = roots[i], assetPath = assetItem.assetPath, id = assetItem.id, name = roots[i].name });
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.StopAssetEditing();
                assetEditing = false;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                string bundlesRoot = Path.Combine(tempDir, "bundles");
                var targets = new List<Tuple<string, BuildTarget>>
                {
                    Tuple.Create("StandaloneWindows64", BuildTarget.StandaloneWindows64),
                    Tuple.Create("StandaloneLinux64", BuildTarget.StandaloneLinux64)
                };
                string bundleFileName = "scene-" + Guid.NewGuid().ToString("N") + ".bundle";
                var manifestPrefabs = assetItems.Select(item => new NxScenePrefab
                {
                    id = item.id,
                    name = item.name,
                    isFoliage = item.source.GetComponent<NxSceneFoliageTag>() != null || item.source.GetComponentInChildren<NxSceneFoliageTag>(true) != null,
                    bundles = targets.Select(target => new NxScenePlatformBundle { platform = target.Item1, path = "bundles/" + target.Item1 + "/" + bundleFileName, asset = item.assetPath.Replace('\\', '/').ToLowerInvariant() }).ToArray()
                }).ToArray();
                var readabilityStates = MakeSourceMeshesReadable(assetItems);
                try
                {
                    foreach (var target in targets)
                    {
                        string platformLabel = target.Item2 == BuildTarget.StandaloneWindows64 ? T("windows") : T("linux");
                        EditorUtility.DisplayProgressBar(T("exporting"), string.Format(T("building"), platformLabel), 0.5f);
                        string targetRoot = Path.Combine(bundlesRoot, target.Item1);
                        Directory.CreateDirectory(targetRoot);
                        var builds = new[] { new AssetBundleBuild { assetBundleName = bundleFileName, assetNames = assetItems.Select(item => item.assetPath).ToArray() } };
                        var bundleManifest = BuildPipeline.BuildAssetBundles(targetRoot, builds, BuildAssetBundleOptions.ChunkBasedCompression, target.Item2);
                        if (bundleManifest == null || builds.Any(build => !File.Exists(Path.Combine(targetRoot, build.assetBundleName)))) throw new InvalidOperationException(string.Format(T("bundleBuildFailed"), platformLabel));
                    }
                }
                finally
                {
                    RestoreSourceMeshReadability(readabilityStates);
                }

                string packageName = string.IsNullOrWhiteSpace(m_packageName) ? T("packageDefault") : m_packageName.Trim();
                var manifest = new NxSceneManifest
                {
                    version = "4",
                    sceneName = SceneManager.GetActiveScene().name,
                    packageId = m_packageId.Trim(),
                    packageName = packageName,
                    packageVersion = m_packageVersion.Trim(),
                    author = m_author.Trim(),
                    platforms = targets.Select(target => target.Item1).ToArray(),
                    prefabs = manifestPrefabs,
                    instances = BuildInstances(items),
                    lodGroups = BuildLodGroups(items),
                    files = new NxSceneFileEntry[0]
                };
                File.WriteAllText(Path.Combine(tempDir, "manifest.json"), JsonUtility.ToJson(manifest, true));
                if (m_thumbnail && m_source == SourceKind.CurrentScene) TryCaptureThumbnail(Path.Combine(tempDir, "thumbnail.png"));
                manifest.files = BuildInventory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "manifest.json"), JsonUtility.ToJson(manifest, true));

                string outputPath = Path.Combine(m_outputFolder, Sanitize(packageName) + ".nxscene");
                if (File.Exists(outputPath)) File.Delete(outputPath);
                CreateZip(outputPath, tempDir);
                if (!File.Exists(outputPath) || Directory.Exists(outputPath)) throw new IOException(T("packageMissing"));
                EditorUtility.ClearProgressBar();
                EditorUtility.RevealInFinder(m_outputFolder);
                EditorUtility.DisplayDialog(T("success"), outputPath, T("openFolder"));
            }
            catch (Exception exception)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(T("failed"), exception.Message, T("ok"));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (assetEditing)
                {
                    AssetDatabase.StopAssetEditing();
                    assetEditing = false;
                }
                AssetDatabase.StartAssetEditing();
                foreach (var path in createdAssets) if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
                if (AssetDatabase.IsValidFolder(tempAssetRoot)) AssetDatabase.DeleteAsset(tempAssetRoot);
                AssetDatabase.SaveAssets();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private List<GameObject> GetRoots()
        {
            if (m_source == SourceKind.CurrentScene) return GetSceneObjects();
            if (!AssetDatabase.IsValidFolder(m_prefabFolder)) return new List<GameObject>();
            return AssetDatabase.FindAssets("t:Prefab", new[] { m_prefabFolder }).Select(AssetDatabase.GUIDToAssetPath).Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)).Where(root => root != null).OrderBy(root => root.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<GameObject> GetSceneObjects()
        {
            var objects = new List<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects().Where(root => root != null && root.hideFlags == HideFlags.None))
            {
                AddSceneObject(root.transform, objects);
            }
            return objects;
        }

        private static void AddSceneObject(Transform source, List<GameObject> objects)
        {
            objects.Add(source.gameObject);
            foreach (Transform child in source)
            {
                AddSceneObject(child, objects);
            }
        }

        private static string BuildAssetSignature(GameObject root)
        {
            var builder = new StringBuilder();
            builder.Append(root.activeSelf).Append('|').Append(root.layer).Append('|').Append(root.tag);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var transform in transforms)
            {
                builder.Append("|T|").Append(GetRelativePath(root.transform, transform));
                if (transform != root.transform)
                {
                    builder.Append('|').Append(transform.localPosition).Append('|').Append(transform.localRotation).Append('|').Append(transform.localScale);
                }
                builder.Append('|').Append(transform.gameObject.activeSelf).Append('|').Append(transform.gameObject.layer).Append('|').Append(transform.gameObject.tag);
            }

            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (component == null || component is Transform || component is MonoBehaviour || component is LODGroup) continue;
                builder.Append("|C|").Append(GetRelativePath(root.transform, component.transform));
                builder.Append('|').Append(component.GetType().AssemblyQualifiedName);
                builder.Append('|').Append(EditorJsonUtility.ToJson(component));
            }

            using var sha = SHA1.Create();
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        private static string GetRelativePath(Transform root, Transform current)
        {
            var names = new Stack<string>();
            var cursor = current;
            while (cursor != null && cursor != root)
            {
                names.Push(cursor.name);
                cursor = cursor.parent;
            }
            return string.Join("/", names);
        }

        private static GameObject CreateSingleObjectClone(GameObject source)
        {
            var clone = new GameObject(source.name)
            {
                layer = source.layer,
                tag = source.tag
            };
            clone.SetActive(source.activeSelf);
            foreach (var component in source.GetComponents<Component>())
            {
                if (component == null || component is Transform || component is MonoBehaviour) continue;
                ComponentUtility.CopyComponent(component);
                ComponentUtility.PasteComponentAsNew(clone);
            }
            return clone;
        }

        private static string GetHierarchyPath(Transform source)
        {
            var names = new Stack<string>();
            var current = source;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }

        private NxSceneInstance[] BuildInstances(List<ExportItem> items)
        {
            if (m_source != SourceKind.CurrentScene) return new NxSceneInstance[0];
            var indexes = items.Select((item, index) => new { item.source, index }).ToDictionary(value => value.source.GetInstanceID(), value => value.index);
            return items.Select(item => new NxSceneInstance
            {
                name = item.source.name,
                layer = LayerMask.LayerToName(item.source.layer),
                prefabId = item.id,
                parentIndex = item.source.transform.parent != null && indexes.TryGetValue(item.source.transform.parent.gameObject.GetInstanceID(), out var parentIndex) ? parentIndex : -1,
                parentPath = string.Empty,
                localPosition = item.source.transform.localPosition,
                localRotation = item.source.transform.localRotation,
                localScale = item.source.transform.localScale
            }).ToArray();
        }

        private NxSceneLodGroup[] BuildLodGroups(List<ExportItem> items)
        {
            if (m_source != SourceKind.CurrentScene) return Array.Empty<NxSceneLodGroup>();
            var indexes = items.Select((item, index) => new { item.source, index }).ToDictionary(value => value.source.GetInstanceID(), value => value.index);
            var groups = new List<NxSceneLodGroup>();
            for (int instanceIndex = 0; instanceIndex < items.Count; instanceIndex++)
            {
                var source = items[instanceIndex].source;
                var sourceGroups = source.GetComponents<LODGroup>();
                for (int componentIndex = 0; componentIndex < sourceGroups.Length; componentIndex++)
                {
                    var group = sourceGroups[componentIndex];
                    var sourceLods = group.GetLODs();
                    var levels = new NxSceneLodLevel[sourceLods.Length];
                    for (int lodIndex = 0; lodIndex < sourceLods.Length; lodIndex++)
                    {
                        var sourceRenderers = sourceLods[lodIndex].renderers ?? Array.Empty<Renderer>();
                        var renderers = new List<NxSceneLodRenderer>(sourceRenderers.Length);
                        foreach (var renderer in sourceRenderers)
                        {
                            if (renderer == null) continue;
                            if (!indexes.TryGetValue(renderer.gameObject.GetInstanceID(), out var rendererInstanceIndex))
                                throw new InvalidOperationException(T("lodRefsInvalid"));
                            var rendererComponents = renderer.gameObject.GetComponents<Renderer>();
                            int rendererComponentIndex = Array.IndexOf(rendererComponents, renderer);
                            if (rendererComponentIndex < 0) throw new InvalidOperationException(T("lodRefsInvalid"));
                            renderers.Add(new NxSceneLodRenderer { instanceIndex = rendererInstanceIndex, componentIndex = rendererComponentIndex });
                        }
                        levels[lodIndex] = new NxSceneLodLevel
                        {
                            transitionHeight = sourceLods[lodIndex].screenRelativeTransitionHeight,
                            fadeTransitionWidth = sourceLods[lodIndex].fadeTransitionWidth,
                            renderers = renderers.ToArray()
                        };
                    }
                    groups.Add(new NxSceneLodGroup
                    {
                        instanceIndex = instanceIndex,
                        componentIndex = componentIndex,
                        enabled = group.enabled,
                        fadeMode = (int)group.fadeMode,
                        animateCrossFading = group.animateCrossFading,
                        localReferencePoint = group.localReferencePoint,
                        size = group.size,
                        levels = levels
                    });
                }
            }
            return groups.ToArray();
        }

        private static NxSceneFileEntry[] BuildInventory(string root)
        {
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) && IncludePackageFile(root, path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            return files.Select(path => new NxSceneFileEntry
            {
                path = Relative(root, path).Replace('\\', '/'),
                sha256 = HashFile(path),
                size = new FileInfo(path).Length
            }).ToArray();
        }

        private static string HashFile(string path)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            return Hex(sha.ComputeHash(stream));
        }

        private static bool RequiresScriptStrip(GameObject root)
        {
            return root.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component != null);
        }

        private static List<ModelReadabilityState> MakeSourceMeshesReadable(List<ExportItem> items)
        {
            var states = new List<ModelReadabilityState>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                foreach (var meshFilter in item.source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (meshFilter != null && meshFilter.sharedMesh != null) paths.Add(AssetDatabase.GetAssetPath(meshFilter.sharedMesh));
                }
                foreach (var renderer in item.source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer != null && renderer.sharedMesh != null) paths.Add(AssetDatabase.GetAssetPath(renderer.sharedMesh));
                }
            }

            try
            {
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || importer.isReadable) continue;
                    states.Add(new ModelReadabilityState { path = path, wasReadable = importer.isReadable });
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }
            catch
            {
                RestoreSourceMeshReadability(states);
                throw;
            }
            return states;
        }

        private static void RestoreSourceMeshReadability(List<ModelReadabilityState> states)
        {
            foreach (var state in states)
            {
                var importer = AssetImporter.GetAtPath(state.path) as ModelImporter;
                if (importer == null || importer.isReadable == state.wasReadable) continue;
                importer.isReadable = state.wasReadable;
                importer.SaveAndReimport();
            }
        }

        private static void StripScripts(GameObject root)
        {
            var components = root.GetComponentsInChildren<MonoBehaviour>(true).Where(component => component != null).ToList();
            while (components.Count > 0)
            {
                var component = components.FirstOrDefault(candidate => !components.Any(other => other != candidate && RequiresComponent(other.GetType(), candidate.GetType())));
                if (component == null) component = components[components.Count - 1];
                DestroyImmediate(component);
                components.Remove(component);
            }
        }

        private static bool RequiresComponent(Type componentType, Type requiredType)
        {
            var fields = new[] { "m_Type0", "m_Type1", "m_Type2" };
            foreach (var attribute in componentType.GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>())
            {
                foreach (var fieldName in fields)
                {
                    var field = typeof(RequireComponent).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var type = field == null ? null : field.GetValue(attribute) as Type;
                    if (type != null && (type == requiredType || type.IsAssignableFrom(requiredType) || requiredType.IsAssignableFrom(type))) return true;
                }
            }
            return false;
        }

        private static void CreateZip(string output, string root)
        {
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Relative(root, file).Replace('\\', '/');
                if (!IncludePackageFile(root, file)) continue;
                var entry = archive.CreateEntry(relative, System.IO.Compression.CompressionLevel.NoCompression);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }
        }

        private static bool IncludePackageFile(string root, string file)
        {
            string relative = Relative(root, file).Replace('\\', '/');
            if (file.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".manifest.meta", StringComparison.OrdinalIgnoreCase)) return false;
            return !relative.StartsWith("bundles/", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryCaptureThumbnail(string output)
        {
            var camera = UnityEngine.Object.FindObjectsOfType<Camera>().FirstOrDefault(value => value != null && value.isActiveAndEnabled);
            if (camera == null) return;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new RenderTexture(640, 360, 24);
            var image = new Texture2D(640, 360, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                image.Apply();
                File.WriteAllBytes(output, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                DestroyImmediate(image);
                DestroyImmediate(texture);
            }
        }

        private static string StableId(string assetPath, string name, int index)
        {
            string value = string.IsNullOrEmpty(assetPath) ? SceneManager.GetActiveScene().path + "|" + name + "|" + index : AssetDatabase.AssetPathToGUID(assetPath);
            using var sha = SHA1.Create();
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        private static string Slug(string value)
        {
            var chars = value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray();
            return new string(chars).Trim('-');
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "package";
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid.ToString(), "_");
            return value.Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static string Relative(string root, string path)
        {
            var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(path).Substring(basePath.Length) : Path.GetFileName(path);
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string ToAssetPath(string fullPath)
        {
            var data = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(fullPath);
            return full.StartsWith(data, StringComparison.OrdinalIgnoreCase) ? "Assets/" + full.Substring(data.Length).Replace('\\', '/') : "Assets";
        }

        private static string GetDefaultOutputFolder()
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(folder) ? Application.dataPath : folder;
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private string T(string key)
        {
            var source = Application.systemLanguage == SystemLanguage.Portuguese ? Portuguese : English;
            return source.TryGetValue(key, out var value) ? value : key;
        }

        private void BeginCard(string title)
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void EndCard()
        {
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }
    }
}
#endif
