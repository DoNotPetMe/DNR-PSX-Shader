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
        [SerializeField] bool fixTextureImports = true;
        [SerializeField] List<string> selectedRadials = new List<string>();
        [SerializeField] Material globalPreset;
        [SerializeField] bool globalEffects = true;
        [SerializeField] bool globalColor = true;
        [SerializeField] bool globalLighting = true;
        [SerializeField] bool globalIncludeFolder;
        [SerializeField] bool globalLiveSync;

        MaterialEditor presetEditor;
        int presetFingerprint;
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
            DrawGlobalSection();
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

            fixTextureImports = EditorGUILayout.ToggleLeft(
                new GUIContent("Fix texture alpha import settings",
                    "For see-through materials, enables 'Alpha Is Transparency' (and the alpha channel itself) " +
                    "on the source textures. This is what removes black halos around eyelashes and hair. " +
                    "Changes are listed in the report and the Console."),
                fixTextureImports);

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

        // ------------------------------------------------------- global look
        void DrawGlobalSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("5. Global Look (Optional)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Dial in the PSX look once and push it to every PSX material on the avatar, so all meshes " +
                "match. Each mesh keeps its own textures, tint, transparency and culling — only the " +
                "stylistic settings are shared.", MessageType.None);

            // Adopt an existing preset in the output folder without asking.
            if (globalPreset == null && !string.IsNullOrEmpty(outputFolder))
                globalPreset = AssetDatabase.LoadAssetAtPath<Material>(outputFolder + "/PSX Global Settings.mat");

            EditorGUI.BeginChangeCheck();
            globalPreset = (Material)EditorGUILayout.ObjectField(
                new GUIContent("Preset Material", "The material whose look settings get copied to all the others."),
                globalPreset, typeof(Material), false);
            if (EditorGUI.EndChangeCheck())
                DestroyPresetEditor();

            var targets = CollectPSXMaterials();

            if (globalPreset == null)
            {
                using (new EditorGUI.DisabledScope(targets.Count == 0))
                {
                    if (GUILayout.Button("Create Preset From Current Look", GUILayout.Height(24)))
                        CreatePreset(targets.Count > 0 ? targets[0] : null);
                }
                if (targets.Count == 0)
                    EditorGUILayout.LabelField("Generate PSX materials first.", EditorStyles.miniLabel);
                return;
            }

            if (!PSXGlobalSettings.IsPSXMaterial(globalPreset))
            {
                EditorGUILayout.HelpBox("The preset material must use the DNR/PSX shader.", MessageType.Warning);
                return;
            }

            // Which groups travel.
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Apply Groups", GUILayout.Width(EditorGUIUtility.labelWidth - 2));
                globalEffects = GUILayout.Toggle(globalEffects, "PSX", EditorStyles.miniButtonLeft);
                globalColor = GUILayout.Toggle(globalColor, "Color/CRT", EditorStyles.miniButtonMid);
                globalLighting = GUILayout.Toggle(globalLighting, "Lighting", EditorStyles.miniButtonRight);
            }

            globalIncludeFolder = EditorGUILayout.ToggleLeft(
                new GUIContent("Include every PSX material in the output folder",
                    "Also covers PSX materials that aren't currently on this avatar's renderers."),
                globalIncludeFolder);

            EditorGUI.BeginChangeCheck();
            globalLiveSync = EditorGUILayout.ToggleLeft(
                new GUIContent("Live sync while this window is open",
                    "Push every edit to the preset out to the other materials automatically."),
                globalLiveSync);
            if (EditorGUI.EndChangeCheck() && globalLiveSync)
                presetFingerprint = CurrentFingerprint();

            // The preset is edited through the real material inspector, so the
            // tool never drifts out of sync with the shader's own UI.
            if (presetEditor == null || presetEditor.target != globalPreset)
            {
                DestroyPresetEditor();
                presetEditor = (MaterialEditor)UnityEditor.Editor.CreateEditor(globalPreset);
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                presetEditor.PropertiesGUI();

            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button($"Apply To {targets.Count} Material{(targets.Count == 1 ? "" : "s")}", GUILayout.Height(30)))
                    ApplyGlobal(targets, verbose: true);
            }
            if (GUILayout.Button("Pull Settings From Avatar Into Preset"))
                PullIntoPreset(targets);

            if (globalLiveSync)
            {
                int fingerprint = CurrentFingerprint();
                if (fingerprint != presetFingerprint)
                {
                    presetFingerprint = fingerprint;
                    ApplyGlobal(targets, verbose: false);
                }
            }
        }

        int CurrentFingerprint()
        {
            return PSXGlobalSettings.Fingerprint(globalPreset, globalEffects, globalColor, globalLighting);
        }

        /// <summary>Every PSX material this window should keep in sync.</summary>
        List<Material> CollectPSXMaterials()
        {
            var result = new List<Material>();
            void Add(Material mat)
            {
                if (PSXGlobalSettings.IsPSXMaterial(mat) && mat != globalPreset && !result.Contains(mat))
                    result.Add(mat);
            }

            foreach (var entry in entries.Where(e => e.include && e.renderer != null))
            {
                foreach (var mat in entry.psx) Add(mat);
                foreach (var mat in entry.renderer.sharedMaterials) Add(mat);
            }

            if (globalIncludeFolder && AssetDatabase.IsValidFolder(outputFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { outputFolder }))
                    Add(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            return result;
        }

        void CreatePreset(Material seed)
        {
            try
            {
                globalPreset = PSXGlobalSettings.CreatePreset(outputFolder, seed);
                DestroyPresetEditor();
                presetFingerprint = CurrentFingerprint();
                EditorGUIUtility.PingObject(globalPreset);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Preset Creation Failed", e.Message, "OK");
                Debug.LogException(e);
            }
        }

        void ApplyGlobal(List<Material> targets, bool verbose)
        {
            try
            {
                int count = PSXGlobalSettings.Apply(globalPreset, targets, globalEffects, globalColor, globalLighting);
                if (verbose)
                {
                    EditorUtility.DisplayDialog("PSX Global Look",
                        count == 0
                            ? "No PSX materials to update."
                            : $"Applied the preset's look to {count} material{(count == 1 ? "" : "s")}.",
                        "OK");
                }
                if (count > 0)
                    Debug.Log($"[DNR PSX] Applied global look settings to {count} material(s).");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Apply Failed", e.Message, "OK");
                Debug.LogException(e);
            }
        }

        void PullIntoPreset(List<Material> targets)
        {
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing To Pull", "No PSX materials found on this avatar.", "OK");
                return;
            }
            Undo.RecordObject(globalPreset, "Pull PSX Settings");
            foreach (string property in PSXGlobalSettings.Gather(true, true, true))
                if (targets[0].HasProperty(property) && globalPreset.HasProperty(property))
                    globalPreset.SetFloat(property, targets[0].GetFloat(property));
            PSXShaderGUI.ValidateKeywords(globalPreset);
            EditorUtility.SetDirty(globalPreset);
            AssetDatabase.SaveAssets();
            presetFingerprint = CurrentFingerprint();
            Debug.Log($"[DNR PSX] Pulled look settings from \"{targets[0].name}\" into the preset.");
        }

        void DestroyPresetEditor()
        {
            if (presetEditor != null)
            {
                UnityEngine.Object.DestroyImmediate(presetEditor);
                presetEditor = null;
            }
        }

        void OnDisable()
        {
            DestroyPresetEditor();
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
                var log = new ConversionLog();
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
                                converted = PSXMaterialConverter.Convert(
                                    source, outputFolder, usedPaths, log, fixTextureImports);
                                cache[source] = converted;
                                count++;
                            }
                            entry.psx.Add(converted);
                        }
                    }
                }

                AssetDatabase.SaveAssets();

                ReportConversion(count, log);
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

        /// <summary>
        /// Surfaces conversion notes where the user will actually see them: the
        /// Console gets everything, a dialog gets anything needing attention.
        /// </summary>
        void ReportConversion(int count, ConversionLog log)
        {
            var attention = log.warnings.Where(w => !w.StartsWith("[info]")).ToList();
            var info = log.warnings.Where(w => w.StartsWith("[info]")).ToList();

            var console = new System.Text.StringBuilder();
            console.AppendLine($"[DNR PSX] Generated/updated {count} PSX material{(count == 1 ? "" : "s")} in {outputFolder}.");
            if (log.textureFixes.Count > 0)
                console.AppendLine($"Enabled alpha transparency on {log.textureFixes.Count} texture(s): " +
                                   string.Join(", ", log.textureFixes));
            foreach (string line in info) console.AppendLine(line);
            foreach (string line in attention) console.AppendLine("WARNING: " + line);
            Debug.Log(console.ToString().TrimEnd());

            var dialog = new System.Text.StringBuilder();
            dialog.AppendLine($"Converted {count} material{(count == 1 ? "" : "s")}.");
            if (log.textureFixes.Count > 0)
                dialog.AppendLine($"\nFixed alpha import settings on {log.textureFixes.Count} texture(s) " +
                                  "so transparent edges don't render black.");

            if (attention.Count > 0)
            {
                dialog.AppendLine("\nNeeds a look:");
                foreach (string line in attention.Take(6))
                    dialog.AppendLine("• " + line);
                if (attention.Count > 6)
                    dialog.AppendLine($"• ...and {attention.Count - 6} more (see the Console).");
                EditorUtility.DisplayDialog("PSX Materials", dialog.ToString(), "OK");
            }
            else
            {
                dialog.Append("\nNo transparency problems detected.");
                EditorUtility.DisplayDialog("PSX Materials", dialog.ToString(), "Nice");
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
