using UnityEngine;

namespace MMDPlayer
{
    /// <summary>
    /// Small safety net for imported MMD materials. UMT's <c>PMXMaterialBuilder</c> picks a shader from
    /// lilToon → URP Unlit → built-in Unlit, so normally every material gets a valid shader. But if a
    /// package/shader is missing or the import produced a null/error shader, models render magenta.
    ///
    /// This helper walks the renderers under a model root and replaces any material whose shader is
    /// missing / error with a lightweight fallback, copying the main texture and base color across so
    /// the model stays visible. It only touches genuinely broken materials — a valid lilToon/Unlit
    /// material is left untouched.
    /// </summary>
    public static class MMDShaderFixer
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// Fixes broken/error shaders on every renderer under <paramref name="root"/>. Returns the number
        /// of materials replaced, or -1 when <paramref name="root"/> is null / no fallback shader exists.
        /// </summary>
        public static int FixMaterials(GameObject root)
        {
            if (root == null)
            {
                return -1;
            }

            Shader fallback = Shader.Find("Standard");
            if (fallback == null)
            {
                // Built-in "Standard" is unavailable (e.g. a URP-only project). Try the Unlit fallback
                // that UMT uses as its last resort; if that is missing too there is nothing safe to do.
                fallback = Shader.Find("Unlit/Texture");
            }
            if (fallback == null)
            {
                return -1;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return 0;
            }

            int replaced = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                Material[] shared = renderer.sharedMaterials;
                if (shared == null)
                {
                    continue;
                }

                bool anyChanged = false;
                for (int i = 0; i < shared.Length; ++i)
                {
                    Material material = shared[i];
                    if (material == null)
                    {
                        continue;
                    }

                    Shader shader = material.shader;
                    if (IsBrokenShader(shader))
                    {
                        Material replacement = BuildReplacement(material, fallback);
                        if (replacement != null)
                        {
                            shared[i] = replacement;
                            ++replaced;
                            anyChanged = true;
                        }
                    }
                }

                if (anyChanged)
                {
                    renderer.sharedMaterials = shared;
                }
            }

            return replaced;
        }

        /// <summary>
        /// Returns true when a shader is missing (null) or is Unity's error shader, which renders magenta.
        /// </summary>
        private static bool IsBrokenShader(Shader shader)
        {
            if (shader == null)
            {
                return true;
            }

            string name = shader.name;
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            // "Hidden/InternalErrorShader" is the built-in magenta shader Unity shows for missing/misconfigured materials.
            return name.IndexOf("InternalErrorShader", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Hidden/Error", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Material BuildReplacement(Material source, Shader fallback)
        {
            Material replacement = new Material(fallback);
            replacement.name = (source != null ? source.name + "_fixed" : "MMD_Fixed");

            if (source == null)
            {
                return replacement;
            }

            if (source.HasProperty(MainTexId))
            {
                Texture tex = source.GetTexture(MainTexId);
                if (tex != null)
                {
                    replacement.SetTexture(MainTexId, tex);
                }
            }

            if (source.HasProperty(ColorId))
            {
                replacement.SetColor(ColorId, source.GetColor(ColorId));
            }

            if (source.HasProperty("_Color") && replacement.HasProperty("_Color"))
            {
                replacement.SetColor("_Color", source.GetColor("_Color"));
            }

            if (source.HasProperty("_MainTex") && replacement.HasProperty("_MainTex"))
            {
                Texture tex = source.GetTexture("_MainTex");
                if (tex != null)
                {
                    replacement.SetTexture("_MainTex", tex);
                }
            }

            return replacement;
        }
    }
}
