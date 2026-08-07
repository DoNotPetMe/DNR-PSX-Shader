// ============================================================================
// DNR PSX Shader - In-game toggle builder
// Builds everything needed to swap between the avatar's original materials and
// the generated PSX materials from the VRChat action menu:
//   * two animation clips (original materials / PSX materials)
//   * an FX animator layer with a bool-driven two-state machine
//   * a synced, saved bool in the avatar's Expression Parameters
//   * a Toggle control in the chosen Expressions Menu
//
// Everything is idempotent: re-running updates the existing clips, layer,
// parameter and menu control instead of duplicating them.
//
// Only compiled when the VRChat Avatars SDK package (com.vrchat.avatars) is
// present - see the DNR_VRC_AVATARS version define in the asmdef.
// ============================================================================

#if DNR_VRC_AVATARS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DNR.PSX.Editor
{
    public static class PSXToggleBuilder
    {
        public class SlotSwap
        {
            public Renderer renderer;
            public Material[] offMaterials; // original materials (default state)
            public Material[] onMaterials;  // PSX materials
        }

        public class BuildSettings
        {
            public string parameterName = "PSXShader";
            public string controlName = "PSX Shader";
            public string outputFolder = "Assets/DNR PSX Generated";
            public VRCExpressionsMenu targetMenu; // null = avatar's root menu
        }

        const string LayerName = "DNR PSX Toggle";
        const string SettingsMenuName = "PSX Settings";

        /// <summary>
        /// A shader setting that can be exposed as an in-game radial slider.
        /// VRChat animations can only drive material float properties (not
        /// shader keywords), so each radial drives one float and force-enables
        /// whatever keyword its effect needs on the PSX materials.
        /// </summary>
        public class RadialSetting
        {
            public string id;             // stable id used in parameter names
            public string label;          // menu / layer label
            public string property;       // animated material float property
            public float min;             // value at radial = 0
            public float max;             // value at radial = 1
            public float defaultT;        // default radial position (0..1)
            public string enableProperty; // float toggle forced to 1 (optional)
            public string enableKeyword;  // keyword forced on (optional)
        }

        public static readonly RadialSetting[] RadialSettings =
        {
            new RadialSetting { id = "Snap",      label = "Vertex Snap",  property = "_SnapStrength",      min = 0f,    max = 1f,   defaultT = 1f },
            new RadialSetting { id = "Affine",    label = "Affine Warp",  property = "_AffineStrength",    min = 0f,    max = 1f,   defaultT = 1f },
            new RadialSetting { id = "Pixelate",  label = "Pixelation",   property = "_PixelResolution",   min = 512f,  max = 32f,  defaultT = 0.5f,
                                enableProperty = "_Pixelate",   enableKeyword = "_DNR_PIXELATE" },
            new RadialSetting { id = "Crush",     label = "Color Crush",  property = "_ColorBits",         min = 8f,    max = 3f,   defaultT = 0.6f,
                                enableProperty = "_Posterize",  enableKeyword = "_DNR_POSTERIZE" },
            new RadialSetting { id = "Dither",    label = "Dither",       property = "_DitherStrength",    min = 0f,    max = 1f,   defaultT = 1f,
                                enableProperty = "_Posterize",  enableKeyword = "_DNR_POSTERIZE" },
            new RadialSetting { id = "Scanlines", label = "Scanlines",    property = "_ScanlineIntensity", min = 0f,    max = 1f,   defaultT = 0.25f,
                                enableProperty = "_Scanlines",  enableKeyword = "_DNR_SCANLINES" },
            new RadialSetting { id = "DotCrawl",  label = "Dot Crawl",    property = "_DotCrawlIntensity", min = 0f,    max = 1f,   defaultT = 0.5f,
                                enableProperty = "_DotCrawl",   enableKeyword = "_DNR_DOTCRAWL" },
            new RadialSetting { id = "Hue",       label = "Hue Shift",    property = "_HueShift",          min = -180f, max = 180f, defaultT = 0.5f,
                                enableProperty = "_ColorGrade", enableKeyword = "_DNR_COLORGRADE" },
        };

        /// <summary>
        /// Builds or updates the complete toggle. Throws with a user-readable
        /// message on any unrecoverable problem; returns a summary on success.
        /// </summary>
        public static string Build(VRCAvatarDescriptor descriptor, List<SlotSwap> swaps, BuildSettings settings)
        {
            if (descriptor == null)
                throw new InvalidOperationException("The selected avatar has no VRC Avatar Descriptor.");

            // Only slots that actually change matter for the animations.
            var changes = CollectChanges(descriptor.transform, swaps);
            if (changes.Count == 0)
                throw new InvalidOperationException(
                    "No material changes found. Generate PSX materials first (step 2 in the setup window).");

            PSXMaterialConverter.EnsureFolder(settings.outputFolder);
            Undo.RecordObject(descriptor, "Build PSX Toggle");

            AnimationClip offClip = WriteClip(settings.outputFolder + "/PSX Off.anim", changes, useOn: false);
            AnimationClip onClip  = WriteClip(settings.outputFolder + "/PSX On.anim", changes, useOn: true);

            AnimatorController fx = GetOrCreateFXController(descriptor, settings.outputFolder);
            EnsureBoolParameter(fx, settings.parameterName);
            RebuildLayer(fx, offClip, onClip, settings.parameterName);

            EnsureExpressionParameter(descriptor, settings.parameterName, settings.outputFolder);
            EnsureMenuControl(descriptor, settings);

            EditorUtility.SetDirty(descriptor);
            AssetDatabase.SaveAssets();
            if (descriptor.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(descriptor.gameObject.scene);

            return $"Toggle built successfully.\n\n" +
                   $"• Animated material slots: {changes.Count}\n" +
                   $"• FX layer: \"{LayerName}\" on {fx.name}\n" +
                   $"• Parameter: \"{settings.parameterName}\" (bool, synced, saved)\n" +
                   $"• Menu control: \"{settings.controlName}\"\n\n" +
                   $"Assets were saved to {settings.outputFolder}.";
        }

        // ------------------------------------------------------------ radials

        /// <summary>
        /// Builds or updates in-game radial sliders for the selected settings:
        /// per setting, a motion-time FX layer + synced float parameter + a
        /// Radial Puppet in a "PSX Settings" submenu linked into the target
        /// menu. Also force-enables each setting's keyword on the PSX
        /// materials so the animated float has an effect to drive.
        /// </summary>
        public static string BuildRadials(VRCAvatarDescriptor descriptor, List<SlotSwap> swaps,
            List<string> selectedIds, BuildSettings settings)
        {
            if (descriptor == null)
                throw new InvalidOperationException("The selected avatar has no VRC Avatar Descriptor.");

            var selected = RadialSettings.Where(r => selectedIds.Contains(r.id)).ToList();
            if (selected.Count == 0)
                throw new InvalidOperationException("No settings selected. Tick at least one radial to build.");
            if (selected.Count > VRCExpressionsMenu.MAX_CONTROLS)
                throw new InvalidOperationException($"Pick at most {VRCExpressionsMenu.MAX_CONTROLS} radials (one submenu page).");

            // Renderers that carry PSX materials, and the materials themselves.
            var psxMaterials = new HashSet<Material>();
            var renderers = new List<Renderer>();
            foreach (var swap in swaps)
            {
                if (swap.renderer == null || swap.onMaterials == null) continue;
                if (!swap.renderer.transform.IsChildOf(descriptor.transform)) continue;
                bool hasPSX = false;
                foreach (var mat in swap.onMaterials)
                {
                    if (mat != null && mat.shader != null && mat.shader.name == PSXMaterialConverter.ShaderName)
                    {
                        psxMaterials.Add(mat);
                        hasPSX = true;
                    }
                }
                if (hasPSX)
                    renderers.Add(swap.renderer);
            }
            if (renderers.Count == 0)
                throw new InvalidOperationException(
                    "No PSX materials found. Generate PSX materials first (step 2 in the setup window).");

            PSXMaterialConverter.EnsureFolder(settings.outputFolder);
            Undo.RecordObject(descriptor, "Build PSX Radials");

            // Check the parameter budget up front (8 bits per new float).
            EnsureRadialParameterBudget(descriptor, selected, settings);

            AnimatorController fx = GetOrCreateFXController(descriptor, settings.outputFolder);
            bool writeDefaults = DetectWriteDefaults(fx);
            VRCExpressionsMenu settingsMenu = GetOrCreateSettingsMenu(settings);

            foreach (var setting in selected)
            {
                string param = $"{settings.parameterName}/{setting.id}";

                // Make sure the effect is actually on, and its default float
                // matches the radial's default position.
                foreach (var mat in psxMaterials)
                {
                    if (!string.IsNullOrEmpty(setting.enableProperty))
                        mat.SetFloat(setting.enableProperty, 1f);
                    if (!string.IsNullOrEmpty(setting.enableKeyword))
                        mat.EnableKeyword(setting.enableKeyword);
                    mat.SetFloat(setting.property, Mathf.Lerp(setting.min, setting.max, setting.defaultT));
                    EditorUtility.SetDirty(mat);
                }

                AnimationClip clip = WriteRadialClip(
                    $"{settings.outputFolder}/PSX Radial {PSXMaterialConverter.Sanitize(setting.label)}.anim",
                    descriptor.transform, renderers, setting);

                EnsureFloatParameter(fx, param);
                RebuildRadialLayer(fx, clip, param, $"DNR PSX Radial {setting.label}", writeDefaults);
                EnsureExpressionFloat(descriptor, param, setting.defaultT, settings.outputFolder);
                EnsureRadialControl(settingsMenu, setting.label, param);
            }

            EnsureSubmenuLink(descriptor, settingsMenu, settings);

            EditorUtility.SetDirty(descriptor);
            AssetDatabase.SaveAssets();
            if (descriptor.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(descriptor.gameObject.scene);

            return $"Built {selected.Count} in-game radial{(selected.Count == 1 ? "" : "s")}:\n\n" +
                   string.Join("\n", selected.Select(s => $"• {s.label}")) +
                   $"\n\nThey live in the \"{SettingsMenuName}\" submenu and cost " +
                   $"{selected.Count * 8} sync bits total. Radials affect the PSX materials, so they " +
                   "only do something visible while the PSX toggle is on.";
        }

        static void EnsureRadialParameterBudget(VRCAvatarDescriptor descriptor,
            List<RadialSetting> selected, BuildSettings settings)
        {
            VRCExpressionParameters parameters = descriptor.expressionParameters;
            if (parameters == null)
                return; // asset will be created with plenty of room

            int needed = 0;
            foreach (var setting in selected)
            {
                string param = $"{settings.parameterName}/{setting.id}";
                bool exists = parameters.parameters != null &&
                              parameters.parameters.Any(p => p != null && p.name == param);
                if (!exists)
                    needed += VRCExpressionParameters.TypeCost(VRCExpressionParameters.ValueType.Float);
            }
            int total = parameters.CalcTotalCost() + needed;
            if (total > VRCExpressionParameters.MAX_PARAMETER_COST)
                throw new InvalidOperationException(
                    $"Not enough Expression Parameter space: {parameters.CalcTotalCost()} bits used, " +
                    $"{needed} more needed, max {VRCExpressionParameters.MAX_PARAMETER_COST}. " +
                    "Untick some radials or free up parameter space.");
        }

        static AnimationClip WriteRadialClip(string path, Transform root, List<Renderer> renderers, RadialSetting setting)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew)
            {
                clip = new AnimationClip();
            }
            else
            {
                clip.ClearCurves();
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            }
            clip.name = System.IO.Path.GetFileNameWithoutExtension(path);

            foreach (var renderer in renderers)
            {
                var binding = EditorCurveBinding.FloatCurve(
                    AnimationUtility.CalculateTransformPath(renderer.transform, root),
                    renderer.GetType(),
                    "material." + setting.property);
                AnimationUtility.SetEditorCurve(clip, binding,
                    AnimationCurve.Linear(0f, setting.min, 1f, setting.max));
            }

            if (isNew)
                AssetDatabase.CreateAsset(clip, path);
            else
                EditorUtility.SetDirty(clip);
            return clip;
        }

        static void EnsureFloatParameter(AnimatorController controller, string name)
        {
            var existing = controller.parameters.FirstOrDefault(p => p.name == name);
            if (existing != null)
            {
                if (existing.type == AnimatorControllerParameterType.Float)
                    return;
                controller.RemoveParameter(existing);
            }
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        // One state whose motion time is driven directly by the float
        // parameter - the standard way to bind a radial to a 0..1 sweep.
        static void RebuildRadialLayer(AnimatorController controller, AnimationClip clip,
            string parameter, string layerName, bool writeDefaults)
        {
            RemoveLayer(controller, layerName);

            var stateMachine = new AnimatorStateMachine
            {
                name = layerName,
                hideFlags = HideFlags.HideInHierarchy
            };
            if (AssetDatabase.Contains(controller))
                AssetDatabase.AddObjectToAsset(stateMachine, controller);

            var layer = new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 1f,
                stateMachine = stateMachine
            };

            AnimatorState state = stateMachine.AddState(layerName, new Vector3(260, 120));
            state.motion = clip;
            state.writeDefaultValues = writeDefaults;
            state.timeParameterActive = true;
            state.timeParameter = parameter;

            stateMachine.defaultState = state;
            controller.AddLayer(layer);
            EditorUtility.SetDirty(controller);
        }

        static void EnsureExpressionFloat(VRCAvatarDescriptor descriptor, string name, float defaultValue, string folder)
        {
            VRCExpressionParameters parameters = descriptor.expressionParameters;
            if (parameters == null)
            {
                parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                parameters.parameters = new VRCExpressionParameters.Parameter[0];
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/PSX Expression Parameters.asset");
                AssetDatabase.CreateAsset(parameters, path);
                descriptor.expressionParameters = parameters;
                descriptor.customExpressions = true;
            }

            var list = parameters.parameters?.ToList() ?? new List<VRCExpressionParameters.Parameter>();
            var existing = list.FirstOrDefault(p => p != null && p.name == name);
            if (existing != null)
            {
                existing.valueType = VRCExpressionParameters.ValueType.Float;
                existing.defaultValue = defaultValue;
                existing.saved = true;
                existing.networkSynced = true;
            }
            else
            {
                int cost = parameters.CalcTotalCost() + VRCExpressionParameters.TypeCost(VRCExpressionParameters.ValueType.Float);
                if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
                    throw new InvalidOperationException(
                        $"Not enough Expression Parameter space for \"{name}\" " +
                        $"({parameters.CalcTotalCost()}/{VRCExpressionParameters.MAX_PARAMETER_COST} bits used, 8 more needed).");

                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = name,
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = defaultValue,
                    saved = true,
                    networkSynced = true
                });
            }
            parameters.parameters = list.ToArray();
            EditorUtility.SetDirty(parameters);
        }

        static VRCExpressionsMenu GetOrCreateSettingsMenu(BuildSettings settings)
        {
            string path = settings.outputFolder + "/PSX Settings Menu.asset";
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if (menu == null)
            {
                menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                AssetDatabase.CreateAsset(menu, path);
            }
            return menu;
        }

        static void EnsureRadialControl(VRCExpressionsMenu menu, string label, string parameter)
        {
            var existing = menu.controls.FirstOrDefault(c =>
                c != null &&
                c.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet &&
                c.subParameters != null && c.subParameters.Length > 0 &&
                c.subParameters[0] != null && c.subParameters[0].name == parameter);

            if (existing != null)
            {
                existing.name = label;
            }
            else
            {
                if (menu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
                    throw new InvalidOperationException(
                        $"The \"{SettingsMenuName}\" submenu is full ({VRCExpressionsMenu.MAX_CONTROLS} controls).");

                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = label,
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = parameter } }
                });
            }
            EditorUtility.SetDirty(menu);
        }

        static void EnsureSubmenuLink(VRCAvatarDescriptor descriptor, VRCExpressionsMenu settingsMenu, BuildSettings settings)
        {
            VRCExpressionsMenu parent = settings.targetMenu;
            if (parent == null)
            {
                parent = descriptor.expressionsMenu;
                if (parent == null)
                {
                    parent = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    string path = AssetDatabase.GenerateUniqueAssetPath(settings.outputFolder + "/PSX Expressions Menu.asset");
                    AssetDatabase.CreateAsset(parent, path);
                    descriptor.expressionsMenu = parent;
                    descriptor.customExpressions = true;
                }
            }
            if (parent == settingsMenu)
                return; // user pointed the target at the settings menu itself

            var existing = parent.controls.FirstOrDefault(c =>
                c != null &&
                c.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                (c.subMenu == settingsMenu || c.name == SettingsMenuName));

            if (existing != null)
            {
                existing.name = SettingsMenuName;
                existing.subMenu = settingsMenu;
            }
            else
            {
                if (parent.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
                    throw new InvalidOperationException(
                        $"The target menu \"{parent.name}\" is full ({VRCExpressionsMenu.MAX_CONTROLS} controls), " +
                        $"so the \"{SettingsMenuName}\" submenu cannot be added. Pick a different menu or free a slot.");

                parent.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = SettingsMenuName,
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = settingsMenu
                });
            }
            EditorUtility.SetDirty(parent);
        }

        // -------------------------------------------------------------- clips
        class SlotChange
        {
            public string path;
            public Type rendererType;
            public int slot;
            public Material off;
            public Material on;
        }

        static List<SlotChange> CollectChanges(Transform root, List<SlotSwap> swaps)
        {
            var changes = new List<SlotChange>();
            foreach (var swap in swaps)
            {
                if (swap.renderer == null || swap.offMaterials == null || swap.onMaterials == null)
                    continue;
                if (!swap.renderer.transform.IsChildOf(root))
                    continue;

                int count = Mathf.Min(swap.offMaterials.Length, swap.onMaterials.Length);
                for (int i = 0; i < count; i++)
                {
                    Material off = swap.offMaterials[i];
                    Material on = swap.onMaterials[i];
                    if (off == null || on == null || off == on)
                        continue;

                    changes.Add(new SlotChange
                    {
                        path = AnimationUtility.CalculateTransformPath(swap.renderer.transform, root),
                        rendererType = swap.renderer.GetType(),
                        slot = i,
                        off = off,
                        on = on
                    });
                }
            }
            return changes;
        }

        static AnimationClip WriteClip(string path, List<SlotChange> changes, bool useOn)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew)
            {
                clip = new AnimationClip();
            }
            else
            {
                clip.ClearCurves();
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            }
            clip.name = System.IO.Path.GetFileNameWithoutExtension(path);

            foreach (var change in changes)
            {
                var binding = EditorCurveBinding.PPtrCurve(
                    change.path, change.rendererType, $"m_Materials.Array.data[{change.slot}]");
                var keyframes = new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = useOn ? change.on : change.off }
                };
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            }

            if (isNew)
                AssetDatabase.CreateAsset(clip, path);
            else
                EditorUtility.SetDirty(clip);
            return clip;
        }

        // ---------------------------------------------------------- animator
        static AnimatorController GetOrCreateFXController(VRCAvatarDescriptor descriptor, string folder)
        {
            var layers = descriptor.baseAnimationLayers;
            int index = Array.FindIndex(layers, l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            if (index < 0)
                throw new InvalidOperationException(
                    "This avatar descriptor has no FX playable layer slot. Is it a valid SDK3 avatar?");

            var layer = layers[index];
            if (!layer.isDefault && layer.animatorController is AnimatorController existing)
                return existing;

            if (!layer.isDefault && layer.animatorController != null)
                throw new InvalidOperationException(
                    "The avatar's FX layer uses an Animator Override Controller, which this tool cannot edit. " +
                    "Assign a regular Animator Controller and try again.");

            // Default slot (even if a controller shows there, it is the SDK's
            // shared asset - never edit that): create a fresh FX controller.
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/PSX FX Controller.controller");
            AnimatorController fx = AnimatorController.CreateAnimatorControllerAtPath(path);

            layer.animatorController = fx;
            layer.isDefault = false;
            layers[index] = layer;
            descriptor.baseAnimationLayers = layers;
            descriptor.customizeAnimationLayers = true;
            return fx;
        }

        static void EnsureBoolParameter(AnimatorController controller, string name)
        {
            var existing = controller.parameters.FirstOrDefault(p => p.name == name);
            if (existing != null)
            {
                if (existing.type == AnimatorControllerParameterType.Bool)
                    return;
                controller.RemoveParameter(existing);
            }
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        }

        static void RebuildLayer(AnimatorController controller, AnimationClip offClip, AnimationClip onClip, string parameter)
        {
            RemoveLayer(controller, LayerName);

            bool writeDefaults = DetectWriteDefaults(controller);

            var stateMachine = new AnimatorStateMachine
            {
                name = LayerName,
                hideFlags = HideFlags.HideInHierarchy
            };
            if (AssetDatabase.Contains(controller))
                AssetDatabase.AddObjectToAsset(stateMachine, controller);

            var layer = new AnimatorControllerLayer
            {
                name = LayerName,
                defaultWeight = 1f,
                stateMachine = stateMachine
            };

            AnimatorState offState = stateMachine.AddState("PSX Off", new Vector3(260, 120));
            offState.motion = offClip;
            offState.writeDefaultValues = writeDefaults;

            AnimatorState onState = stateMachine.AddState("PSX On", new Vector3(260, 220));
            onState.motion = onClip;
            onState.writeDefaultValues = writeDefaults;

            AnimatorStateTransition toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.exitTime = 0f;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameter);

            AnimatorStateTransition toOff = onState.AddTransition(offState);
            toOff.hasExitTime = false;
            toOff.exitTime = 0f;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);

            stateMachine.defaultState = offState;
            controller.AddLayer(layer);
            EditorUtility.SetDirty(controller);
        }

        static void RemoveLayer(AnimatorController controller, string name)
        {
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name != name)
                    continue;

                AnimatorStateMachine sm = layers[i].stateMachine;
                controller.RemoveLayer(i);
                if (sm != null)
                {
                    foreach (var child in sm.states)
                    {
                        if (child.state == null) continue;
                        foreach (var t in child.state.transitions)
                            if (t != null) UnityEngine.Object.DestroyImmediate(t, true);
                        UnityEngine.Object.DestroyImmediate(child.state, true);
                    }
                    UnityEngine.Object.DestroyImmediate(sm, true);
                }
                return;
            }
        }

        /// <summary>
        /// Matches the Write Defaults convention already used on this avatar so
        /// the new layer does not introduce mixed WD (a common animation bug
        /// source). Falls back to WD off, which is safe here because both
        /// states animate the exact same property set.
        /// </summary>
        static bool DetectWriteDefaults(AnimatorController controller)
        {
            int on = 0, off = 0;
            foreach (var layer in controller.layers)
                CountWriteDefaults(layer.stateMachine, ref on, ref off);
            return on > off;
        }

        static void CountWriteDefaults(AnimatorStateMachine sm, ref int on, ref int off)
        {
            if (sm == null) return;
            foreach (var child in sm.states)
            {
                if (child.state == null) continue;
                if (child.state.writeDefaultValues) on++; else off++;
            }
            foreach (var child in sm.stateMachines)
                CountWriteDefaults(child.stateMachine, ref on, ref off);
        }

        // -------------------------------------------------------- parameters
        static void EnsureExpressionParameter(VRCAvatarDescriptor descriptor, string name, string folder)
        {
            VRCExpressionParameters parameters = descriptor.expressionParameters;
            if (parameters == null)
            {
                parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                parameters.parameters = new VRCExpressionParameters.Parameter[0];
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/PSX Expression Parameters.asset");
                AssetDatabase.CreateAsset(parameters, path);
                descriptor.expressionParameters = parameters;
                descriptor.customExpressions = true;
            }

            var list = parameters.parameters?.ToList() ?? new List<VRCExpressionParameters.Parameter>();
            var existing = list.FirstOrDefault(p => p != null && p.name == name);
            if (existing != null)
            {
                existing.valueType = VRCExpressionParameters.ValueType.Bool;
                existing.saved = true;
                existing.networkSynced = true;
            }
            else
            {
                int cost = parameters.CalcTotalCost() + VRCExpressionParameters.TypeCost(VRCExpressionParameters.ValueType.Bool);
                if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
                    throw new InvalidOperationException(
                        $"Not enough space in the avatar's Expression Parameters " +
                        $"({parameters.CalcTotalCost()}/{VRCExpressionParameters.MAX_PARAMETER_COST} bits used, 1 more needed). " +
                        "Free up parameter space and try again.");

                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = name,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true
                });
            }
            parameters.parameters = list.ToArray();
            EditorUtility.SetDirty(parameters);
        }

        // -------------------------------------------------------------- menu
        static void EnsureMenuControl(VRCAvatarDescriptor descriptor, BuildSettings settings)
        {
            VRCExpressionsMenu menu = settings.targetMenu;
            if (menu == null)
            {
                menu = descriptor.expressionsMenu;
                if (menu == null)
                {
                    menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    string path = AssetDatabase.GenerateUniqueAssetPath(settings.outputFolder + "/PSX Expressions Menu.asset");
                    AssetDatabase.CreateAsset(menu, path);
                    descriptor.expressionsMenu = menu;
                    descriptor.customExpressions = true;
                }
            }

            var existing = menu.controls.FirstOrDefault(c =>
                c != null &&
                c.type == VRCExpressionsMenu.Control.ControlType.Toggle &&
                c.parameter != null && c.parameter.name == settings.parameterName);

            if (existing != null)
            {
                existing.name = settings.controlName;
                existing.value = 1f;
            }
            else
            {
                if (menu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
                    throw new InvalidOperationException(
                        $"The target menu \"{menu.name}\" is full ({VRCExpressionsMenu.MAX_CONTROLS} controls). " +
                        "Pick a different menu in the setup window, or free a slot.");

                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = settings.controlName,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = settings.parameterName },
                    value = 1f
                });
            }
            EditorUtility.SetDirty(menu);
        }
    }
}
#endif
