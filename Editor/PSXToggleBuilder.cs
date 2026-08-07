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
