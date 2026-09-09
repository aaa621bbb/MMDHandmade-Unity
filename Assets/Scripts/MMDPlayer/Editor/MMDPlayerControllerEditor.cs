using UnityEditor;
using UnityEngine;

namespace MMDPlayer.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="MMDPlayerController"/>. Adds a "Refresh Library" button that
    /// re-scans <c>Assets/MMDResources</c> and rebuilds the <see cref="MMDAssetLibrary"/>, so the user
    /// never needs to drop into a menu after importing a .pmx/.vmd.
    /// </summary>
    [CustomEditor(typeof(MMDPlayerController))]
    public sealed class MMDPlayerControllerEditor : UnityEditor.Editor
    {
        private bool _showProgress;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "把 .pmx（含贴图）放 Assets/MMDResources/Models，把 .vmd 放 Assets/MMDResources/Motions，然后点下面的 Refresh Library。",
                MessageType.Info);

            if (GUILayout.Button("Refresh Library", GUILayout.Height(28)))
            {
                RefreshLibrary();
            }

            if (_showProgress)
            {
                EditorGUILayout.LabelField("可在 Console 查看“找到 N 个模型 / M 个动作”。");
            }
        }

        private void RefreshLibrary()
        {
            _showProgress = true;
            MMDResourcesScanner.RefreshLibrary();
        }
    }
}
