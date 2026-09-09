using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UMT;
using UMT.Editor;

namespace MMDPlayer.Editor
{
    /// <summary>
    /// Editor tool that scans <c>Assets/MMDResources/Models/*.pmx</c> and <c>Assets/MMDResources/Motions/*.vmd</c>,
    /// converts every VMD into a playable <see cref="AnimationClip"/> via UMT's own converter, and writes the
    /// results into a <see cref="MMDAssetLibrary"/> asset the runtime/UI reads.
    ///
    /// The VMD-to-clip conversion reuses UMT's editor pipeline (<see cref="VMDAnimationClipConverter.Convert"/>
    /// + <see cref="VMDClipDataBuilder.BuildAnimationClip"/>); we do not hand-author any curves.
    ///
    /// Matching rule: a .vmd is considered for a model when the VMD's target model name (from its header)
    /// matches the model, OR when the VMD lists no model name (many dances are model-agnostic). When a VMD
    /// matches more than one model we convert it once per model.
    /// </summary>
    public static class MMDResourcesScanner
    {
        public const string ModelsFolder = "Assets/MMDResources/Models";
        public const string MotionsFolder = "Assets/MMDResources/Motions";
        public const string DefaultLibraryPath = "Assets/MMDResources/Library.asset";

        [MenuItem("Tools/MMD Player/Refresh Library")]
        public static void RefreshFromMenu()
        {
            RefreshLibrary();
        }

        /// <summary>
        /// Scans the resource folders and rebuilds the library asset. Returns the number of models and
        /// motions found, or (-1, -1) on failure. Logs a summary to the Console.
        /// </summary>
        public static (int models, int motions) RefreshLibrary()
        {
            try
            {
                EnsureFolders();

                List<PMXModel> models = FindPMXModels();

                MMDAssetLibrary library = LoadOrCreateLibrary();
                library.Clear();

                int totalMotions = 0;
                foreach (PMXModel model in models)
                {
                    MMDAssetLibrary.ModelEntry entry = new MMDAssetLibrary.ModelEntry
                    {
                        model = model,
                        displayName = GetDisplayName(model),
                        sourcePath = AssetDatabase.GetAssetPath(model),
                    };

                    // Find and convert motions for this model.
                    List<AnimationClip> motions = BuildMotionsForModel(model);
                    entry.motions = motions;
                    totalMotions += motions.Count;

                    library.entries.Add(entry);
                }

                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();

                string message = string.Format("找到 {0} 个模型 / {1} 个动作", models.Count, totalMotions);
                Debug.Log("[MMD Player] " + message);

                // Refresh any open inspector.
                Selection.activeObject = library;
                EditorGUIUtility.PingObject(library);
                return (models.Count, totalMotions);
            }
            catch (Exception e)
            {
                Debug.LogError("[MMD Player] Refresh Library failed: " + e);
                return (-1, -1);
            }
        }

        // ---------------------------------------------------------------------
        //  Discovery
        // ---------------------------------------------------------------------

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MMDResources"))
            {
                AssetDatabase.CreateFolder("Assets", "MMDResources");
            }
            if (!AssetDatabase.IsValidFolder(ModelsFolder))
            {
                AssetDatabase.CreateFolder("Assets/MMDResources", "Models");
            }
            if (!AssetDatabase.IsValidFolder(MotionsFolder))
            {
                AssetDatabase.CreateFolder("Assets/MMDResources", "Motions");
            }
        }

        private static List<PMXModel> FindPMXModels()
        {
            List<PMXModel> result = new List<PMXModel>();
            string[] guids = AssetDatabase.FindAssets("t:PMXModel", new[] { ModelsFolder });
            if (guids == null)
            {
                return result;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PMXModel model = AssetDatabase.LoadAssetAtPath<PMXModel>(path);
                if (model != null)
                {
                    result.Add(model);
                }
            }

            return result;
        }

        private static List<VMDAnimation> FindVMDAnimations()
        {
            List<VMDAnimation> result = new List<VMDAnimation>();
            string[] guids = AssetDatabase.FindAssets("t:VMDAnimation", new[] { MotionsFolder });
            if (guids == null)
            {
                return result;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                VMDAnimation vmd = AssetDatabase.LoadAssetAtPath<VMDAnimation>(path);
                if (vmd != null)
                {
                    result.Add(vmd);
                }
            }

            return result;
        }

        // ---------------------------------------------------------------------
        //  Conversion
        // ---------------------------------------------------------------------

        private static List<AnimationClip> BuildMotionsForModel(PMXModel model)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            if (model == null)
            {
                return clips;
            }

            List<VMDAnimation> allVMDs = FindVMDAnimations();
            foreach (VMDAnimation vmd in allVMDs)
            {
                if (vmd == null)
                {
                    continue;
                }

                if (!MatchesModel(vmd, model))
                {
                    continue;
                }

                AnimationClip clip = ConvertVMDToClip(vmd, model);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            return clips;
        }

        /// <summary>
        /// A VMD "belongs" to a model when the VMD header names the model (empty name = generic motion,
        /// match any) or when the model name appears in the VMD's model-name field.
        /// </summary>
        private static bool MatchesModel(VMDAnimation vmd, PMXModel model)
        {
            string vmdModelName = vmd.modelName.ToString();
            if (string.IsNullOrWhiteSpace(vmdModelName))
            {
                // Generic dance animations apply to any model. Try converting; the converter logs a
                // warning for bones that don't match rather than throwing.
                return true;
            }

            string modelName = GetDisplayName(model);
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = model.name;
            }

            return modelName.IndexOf(vmdModelName, StringComparison.OrdinalIgnoreCase) >= 0
                || vmdModelName.IndexOf(modelName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static AnimationClip ConvertVMDToClip(VMDAnimation vmd, PMXModel model)
        {
            string vmdName = string.IsNullOrEmpty(vmd.name) ? "Motion" : vmd.name;
            string targetPath = GetClipAssetPath(vmdName, model);

            try
            {
                VMDAnimationClipOptions options = new VMDAnimationClipOptions
                {
                    frameRate = MMDConstants.k_VMDNativeFrameRate >= 1f ? MMDConstants.k_VMDNativeFrameRate : 30f,
                    bakeIKToFK = false, // runtime-solved IK keeps the MMDTransformManager authoritative
                    bakePhysicsToFK = false,
                };

                VMDModelClipData clipData = VMDAnimationClipConverter.Convert(vmd, model, options, null);
                if (clipData == null)
                {
                    Debug.LogWarning("[MMD Player] VMD conversion returned null for " + vmdName);
                    return null;
                }

                AnimationClip clip = VMDClipDataBuilder.BuildAnimationClip(clipData, options.frameRate);
                if (clip == null)
                {
                    Debug.LogWarning("[MMD Player] Clip build returned null for " + vmdName);
                    return null;
                }

                clip.name = Path.GetFileNameWithoutExtension(targetPath);
                SaveClipAsset(clip, targetPath);
                return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning(string.Format("[MMD Player] Could not convert VMD '{0}' for model '{1}': {2}", vmdName, model.name, e.Message));
                return null;
            }
        }

        /// <summary>
        /// Writes the clip to the target path, creating the asset the first time or overwriting the
        /// existing asset on a re-scan (so previously stored library references stay valid).
        /// </summary>
        private static void SaveClipAsset(AnimationClip clip, string targetPath)
        {
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, targetPath);
            }
            else
            {
                EditorUtility.CopySerialized(clip, existing);
                EditorUtility.SetDirty(existing);
            }
            AssetDatabase.SaveAssets();
        }

        private static string GetClipAssetPath(string vmdName, PMXModel model)
        {
            // Common output folder keeps clips next to the motions for easy browsing.
            string root = MotionsFolder;
            string modelFileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(model));
            string clipName = SanitizeFileName(vmdName) + "_" + SanitizeFileName(modelFileName);
            string existing = AssetDatabase.FindAssets(
                string.Format("{0} t:AnimationClip", clipName), new[] { root }).Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(string.Format("{0}.anim", clipName), StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            return string.Format("{0}/{1}.anim", root, clipName);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Motion";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }
            return value.Trim();
        }

        // ---------------------------------------------------------------------
        //  Library management
        // ---------------------------------------------------------------------

        private static MMDAssetLibrary LoadOrCreateLibrary()
        {
            MMDAssetLibrary library = AssetDatabase.LoadAssetAtPath<MMDAssetLibrary>(DefaultLibraryPath);
            if (library != null)
            {
                return library;
            }

            EnsureFolders();
            library = ScriptableObject.CreateInstance<MMDAssetLibrary>();
            AssetDatabase.CreateAsset(library, DefaultLibraryPath);
            return library;
        }

        private static string GetDisplayName(PMXModel model)
        {
            if (model == null)
            {
                return string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(model.modelInfo.name.ToString()))
            {
                return model.modelInfo.name.ToString();
            }
            if (!string.IsNullOrWhiteSpace(model.modelInfo.nameEN.ToString()))
            {
                return model.modelInfo.nameEN.ToString();
            }
            return model.name;
        }
    }
}
