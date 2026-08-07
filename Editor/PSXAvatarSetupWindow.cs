// ============================================================================
// DNR PSX Shader - Avatar setup window
// One-stop tool: scan an avatar's renderers, generate PSX materials from the
// existing ones, optionally preview them directly, and build the in-game
// VRChat toggle that swaps between original and PSX materials.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if DNR_VRC_AVATARS
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
#endif

namespace DNR.PSX.Editor
{
    public class PSXAvatarSetupWindow : EditorWindow
    {
        [Serializable]
        class RendererEntry
        {
            public Renderer renderer;
            public bool include = true;
            public List<Material> originals = new List<Material>();
            public List<Material> psx = new List<Material>();
        }

        [SerializeField] GameObject avatar;
        [SerializeField] List<RendererEntry> entries = new List<RendererEntry>();
        [SerializeField] string outputFolder = "";
        [SerializeField] string parameterName = "PSXShader";
        [SerializeField] string controlName = "PSX Shader";
        [SerializeField] bool psxApplied;
        [SerializeField] List<string> selectedRadials = new List<string>();
#if DNR_VRC_AVATARS
        [SerializeField] VRCExpressionsMenu targetMenu;
#endif

        Vector2 scroll;

        [MenuItem("Tools/DNR PSX/Avatar Setup", false, 0)]
        static void Open()
        {
            var window = GetWindow<PSXAvatarSetupWindow>("PSX Avatar Setup");
            window.minSize = new Vector2(380, 480);
        }

        [MenuItem("Tools/DNR PSX/Documentation", false, 100)]
        static void OpenDocs()
        {
            Application.OpenURL("https://github.com/DoNotPetMe/DNR-PSX-Shader");
        }

        // ------------------------------------------------------------ gui
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();
            DrawAvatarField();

            if (avatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Drop your avatar here to get started.\n\n" +
                    "1. Pick which renderers to convert\n" +
                    "2. Generate PSX materials (originals are never modified)\n" +
                    "3. Build the in-game toggle to swap between them", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawRendererList();
            DrawOutputFolder();
            DrawMaterialSection();
            DrawToggleSection();
            DrawRadialsSection();
            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("DNR PSX  •  Avatar Setup", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("v" + PSXShaderGUI.Version, EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(2);
        }

        void DrawAvatarField()
        {
            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
                Scan();
        }

        void DrawRendererList()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("1. Renderers", EditorStyles.boldLabel);

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("No mesh renderers found on this avatar.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", GUILayout.Width(50)))
                    entries.ForEach(e => e.include = true);
                if (GUILayout.Button("None", GUILayout.Width(50)))
                    entries.ForEach(e => e.include = false);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Rescan", GUILayout.Width(70)))
                    Scan();
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var entry in entries)
                {
                    if (entry.renderer == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.include = EditorGUILayout.Toggle(entry.include, GUILayout.Width(18));
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.ObjectField(entry.renderer, typeof(Renderer), true);
                        GUILayout.Label($"{entry.originals.Count} slot{(entry.originals.Count == 1 ? "" : "s")}",
                            EditorStyles.miniLabel, GUILayout.Width(50));
                    }
                }
            }
        }

        void DrawOutputFolder()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string abs = EditorUtility.OpenFolderPanel("Output Folder", "Assets", "");
                    if (!string.IsNullOrEmpty(abs))
                    {
                        if (abs.StartsWith(Application.dataPath))
                            outputFolder = "Assets" + abs.Substring(Application.dataPath.Length);
                        else
                            EditorUtility.DisplayDialog("Invalid Folder",
                                "The output folder must be inside this project's Assets folder.", "OK");
                    }
                }
            }
        }

        void DrawMaterialSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("2. PSX Materials", EditorStyles.boldLabel);

            int generated = entries.Where(e => e.include).SelectMany(e => e.psx).Count(m => m != null);
            if (generated > 0)
                EditorGUILayout.LabelField($"Generated materials ready: {generated}", EditorStyles.miniLabel);

            if (GUILayout.Button(generated > 0 ? "Regenerate PSX Materials" : "Generate PSX Materials", GUILayout.Height(28)))
                GenerateMaterials();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(generated == 0 || psxApplied))
                {
                    if (GUILayout.Button("Preview PSX On Avatar"))
                        ApplyMaterials(psx: true);
                }
                using (new EditorGUI.DisabledScope(!psxApplied))
                {
                    if (GUILayout.Button("Restore Originals"))
                        ApplyMaterials(psx: false);
                }
            }

            if (psxApplied)
                EditorGUILayout.HelpBox(
                    "PSX materials are currently applied for preview. Restore originals before building the " +
                    "toggle so the avatar's default state stays the original look.", MessageType.Warning);
        }

        void DrawToggleSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("3. In-Game Toggle", EditorStyles.boldLabel);

#if DNR_VRC_AVATARS
            var descriptor = avatar != null ? avatar.GetComponent<VRCAvatarDescriptor>() : null;
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox(
                    "No VRC Avatar Descriptor on this object. Select the avatar root to build the toggle.",
                    MessageType.Warning);
                return;
            }

            parameterName = EditorGUILayout.TextField(
                new GUIContent("Parameter Name", "Synced bool added to the avatar's Expression Parameters."),
                parameterName);
            controlName = EditorGUILayout.TextField(
                new GUIContent("Menu Item Name", "Label of the toggle in the Action Menu."),
                controlName);
            targetMenu = (VRCExpressionsMenu)EditorGUILayout.ObjectField(
                new GUIContent("Target Menu", "Menu that receives the toggle. Leave empty to use the avatar's root menu."),
                targetMenu, typeof(VRCExpressionsMenu), false);

            EditorGUILayout.HelpBox(
                "Builds two animation clips, an FX layer, a synced parameter and a menu toggle. " +
                "Default state = original materials; toggling ON swaps every converted slot to its PSX material. " +
                "Re-running updates everything in place.", MessageType.None);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(parameterName)))
            {
                if (GUILayout.Button("Build In-Game Toggle", GUILayout.Height(32)))
                    BuildToggle(descriptor);
            }
#else
            EditorGUILayout.HelpBox(
                "VRChat Avatars SDK (com.vrchat.avatars) not found in this project. " +
                "Install it through the VRChat Creator Companion to build the in-game toggle. " +
                "Material generation above still works without it.", MessageType.Info);
#endif
        }

        void DrawRadialsSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("4. In-Game Setting Radials (Optional)", EditorStyles.boldLabel);

#if DNR_VRC_AVATARS
            var descriptor = avatar != null ? avatar.GetComponent<VRCAvatarDescriptor>() : null;
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("Needs a VRC Avatar Descriptor (see step 3).", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                "Adds radial sliders to your Action Menu (in a \"PSX Settings\" submenu) that adjust the " +
                "shader live in game. Each radial is a synced float: 8 bits of your 256-bit parameter budget. " +
                "Radials drive the PSX materials, so they only show while the PSX toggle is on.",
                MessageType.None);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var setting in PSXToggleBuilder.RadialSettings)
                {
                    bool selected = selectedRadials.Contains(setting.id);
                    bool now = EditorGUILayout.ToggleLeft(setting.label, selected);
                    if (now && !selected) selectedRadials.Add(setting.id);
                    else if (!now && selected) selectedRadials.Remove(setting.id);
                }
            }

            int used = descriptor.expressionParameters != null ? descriptor.expressionParameters.CalcTotalCost() : 0;
            EditorGUILayout.LabelField(
                $"Cost: {selectedRadials.Count * 8} bits for {selectedRadials.Count} radial(s)  •  " +
                $"currently used: {used}/{VRCExpressionParameters.MAX_PARAMETER_COST}",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(selectedRadials.Count == 0 || string.IsNullOrWhiteSpace(parameterName)))
            {
                if (GUILayout.Button("Build Setting Radials", GUILayout.Height(28)))
                    BuildRadials(descriptor);
            }
#else
            EditorGUILayout.HelpBox(
                "Requires the VRChat Avatars SDK (see step 3).", MessageType.None);
#endif
        }

        // ------------------------------------------------------------ actions
        void Scan()
        {
            entries.Clear();
            psxApplied = false;
            if (avatar == null)
                return;

            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;
                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                bool alreadyPSX = materials.Any(m => m != null && m.shader != null && m.shader.name == PSXMaterialConverter.ShaderName);
                if (alreadyPSX)
                    Debug.LogWarning($"[DNR PSX] \"{renderer.name}\" already has PSX materials assigned. " +
                                     "Its current materials will be treated as the toggle's OFF state.", renderer);

                entries.Add(new RendererEntry
                {
                    renderer = renderer,
                    originals = materials.ToList(),
                    psx = materials.Select(_ => (Material)null).ToList()
                });
            }

            // Keep a user-chosen folder; refresh the default when it is still ours.
            if (string.IsNullOrEmpty(outputFolder) || outputFolder.StartsWith("Assets/DNR PSX Generated"))
                outputFolder = $"Assets/DNR PSX Generated/{PSXMaterialConverter.Sanitize(avatar.name)}";
        }

        void GenerateMaterials()
        {
            if (psxApplied)
            {
                EditorUtility.DisplayDialog("Restore First",
                    "PSX materials are currently applied for preview. Restore originals before regenerating, " +
                    "so the original materials are captured correctly.", "OK");
                return;
            }

            try
            {
                var cache = new Dictionary<Material, Material>();
                var usedPaths = new HashSet<string>();
                int count = 0;

                foreach (var entry in entries.Where(e => e.include && e.renderer != null))
                {
                    entry.originals = entry.renderer.sharedMaterials.ToList();
                    entry.psx = new List<Material>();
                    foreach (var source in entry.originals)
                    {
                        if (source == null)
                        {
                            entry.psx.Add(null);
                        }
                        else if (source.shader != null && source.shader.name == PSXMaterialConverter.ShaderName)
                        {
                            entry.psx.Add(source); // already PSX - nothing to convert
                        }
                        else
                        {
                            if (!cache.TryGetValue(source, out var converted))
                            {
                                converted = PSXMaterialConverter.Convert(source, outputFolder, usedPaths);
                                cache[source] = converted;
                                count++;
                            }
                            entry.psx.Add(converted);
                        }
                    }
                }

                AssetDatabase.SaveAssets();

                Debug.Log($"[DNR PSX] Generated/updated {count} PSX material{(count == 1 ? "" : "s")} in {outputFolder}.");
                var folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputFolder);
                if (folderAsset != null)
                    EditorGUIUtility.PingObject(folderAsset);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("PSX Material Generation Failed", e.Message, "OK");
                Debug.LogException(e);
            }
        }

        void ApplyMaterials(bool psx)
        {
            foreach (var entry in entries.Where(e => e.include && e.renderer != null))
            {
                var target = psx ? entry.psx : entry.originals;
                if (target == null || target.Count != entry.renderer.sharedMaterials.Length)
                    continue;
                if (psx)
                {
                    // Don't apply half-generated sets (a PSX slot may only be
                    // null when its original was null too).
                    bool incomplete = false;
                    for (int i = 0; i < target.Count; i++)
                        if (target[i] == null && entry.originals[i] != null) { incomplete = true; break; }
                    if (incomplete)
                        continue;
                }

                Undo.RecordObject(entry.renderer, psx ? "Apply PSX Materials" : "Restore Original Materials");
                entry.renderer.sharedMaterials = target.ToArray();
                EditorUtility.SetDirty(entry.renderer);
            }
            psxApplied = psx;
        }

        void BuildToggle(
#if DNR_VRC_AVATARS
            VRCAvatarDescriptor descriptor
#else
            object descriptor
#endif
        )
        {
#if DNR_VRC_AVATARS
            if (psxApplied)
            {
                EditorUtility.DisplayDialog("Restore First",
                    "Restore the original materials before building the toggle, so the avatar's default " +
                    "state is the original look.", "OK");
                return;
            }

            try
            {
                var swaps = entries
                    .Where(e => e.include && e.renderer != null)
                    .Select(e => new PSXToggleBuilder.SlotSwap
                    {
                        renderer = e.renderer,
                        offMaterials = e.originals.ToArray(),
                        onMaterials = e.psx.ToArray()
                    })
                    .ToList();

                var settings = new PSXToggleBuilder.BuildSettings
                {
                    parameterName = parameterName.Trim(),
                    controlName = string.IsNullOrWhiteSpace(controlName) ? "PSX Shader" : controlName.Trim(),
                    outputFolder = outputFolder,
                    targetMenu = targetMenu
                };

                string report = PSXToggleBuilder.Build(descriptor, swaps, settings);
                EditorUtility.DisplayDialog("PSX Toggle", report, "Nice");
                Debug.Log($"[DNR PSX] {report}");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("PSX Toggle Build Failed", e.Message, "OK");
                Debug.LogException(e);
            }
#endif
        }

#if DNR_VRC_AVATARS
        void BuildRadials(VRCAvatarDescriptor descriptor)
        {
            try
            {
                var swaps = entries
                    .Where(e => e.include && e.renderer != null)
                    .Select(e => new PSXToggleBuilder.SlotSwap
                    {
                        renderer = e.renderer,
                        offMaterials = e.originals.ToArray(),
                        onMaterials = e.psx.ToArray()
                    })
                    .ToList();

                var settings = new PSXToggleBuilder.BuildSettings
                {
                    parameterName = parameterName.Trim(),
                    outputFolder = outputFolder,
                    targetMenu = targetMenu
                };

                string report = PSXToggleBuilder.BuildRadials(descriptor, swaps, selectedRadials, settings);
                EditorUtility.DisplayDialog("PSX Radials", report, "Nice");
                Debug.Log($"[DNR PSX] {report}");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("PSX Radials Build Failed", e.Message, "OK");
                Debug.LogException(e);
            }
        }
#endif
    }
}
