using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UMT;

namespace GiantCity
{
    /// <summary>
    /// Describes one discovered giantess model: a .pmx plus the sibling texture files that share its folder.
    /// </summary>
    public sealed class GiantessModelOption
    {
        public string displayName;
        public string directory;
        public string pmxFileName;

        public string PmxPath => Path.Combine(directory, pmxFileName);

        public bool IsValid => !string.IsNullOrEmpty(pmxFileName) && !string.IsNullOrEmpty(directory);
    }

    /// <summary>
    /// Extension point for a real Android storage picker (e.g. SAF / a third-party file-picker package).
    /// The default implementation scans the app's <c>persistentDataPath/MMDModels</c> folder; to let the user
    /// pick an arbitrary folder on the phone, supply an implementation of this interface. The core
    /// (runtime import + texture loading) is platform-agnostic and fully testable in the editor.
    /// </summary>
    public interface IMobileFilePicker
    {
        /// <summary>Returns reloadable model options from the picker's chosen location.</summary>
        IReadOnlyList<GiantessModelOption> ListModels();

        /// <summary>Prompts the user to pick a model and returns the option, or null when cancelled.</summary>
        GiantessModelOption PickModel();

        /// <summary>Reads a sibling file (e.g. a texture) relative to the model, returning its bytes.</summary>
        byte[] ReadFile(GiantessModelOption option, string relativePath);
    }

    /// <summary>
    /// Runtime giantess model source for Phase 2. Scans a folder for .pmx files, then imports the chosen
    /// one from bytes with a custom <c>loadTextures</c> callback that reads the sibling textures from the
    /// same folder — this is how a model can be dropped onto a phone (in the app's own folder) and swapped
    /// at any time without packing it into the build.
    ///
    /// The import pipeline runs on a <see cref="UMTFrameBudget"/> so heavy work is spread across frames
    /// rather than blocking the main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileModelPicker : MonoBehaviour
    {
        [Tooltip("Sub-folder of Application.persistentDataPath that is scanned for .pmx models by default.")]
        public string modelsFolder = "MMDModels";

        private IMobileFilePicker _externalPicker;

        private const string LastModelKey = "GiantCity.LastModelPath";

        /// <summary>The list of currently discovered models.</summary>
        public IReadOnlyList<GiantessModelOption> AvailableModels => _availableModels;
        private readonly List<GiantessModelOption> _availableModels = new List<GiantessModelOption>();

        /// <summary>Installs an external file picker (e.g. an Android SAF implementation).</summary>
        public void SetExternalPicker(IMobileFilePicker picker)
        {
            _externalPicker = picker;
        }

        /// <summary>Builds the default folder path (<c>persistentDataPath/modelsFolder</c>).</summary>
        public string DefaultFolderPath => Path.Combine(Application.persistentDataPath, modelsFolder);

        /// <summary>Rescans the default persistent-data models folder. Returns the number of models found.</summary>
        public int RefreshFromDefaultFolder()
        {
            return InventoryFromDirectory(DefaultFolderPath);
        }

        /// <summary>Rescans an arbitrary local filesystem directory, building model options.</summary>
        public int InventoryFromDirectory(string directory)
        {
            _availableModels.Clear();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }

            List<string> pmxFiles = new List<string>(Directory.GetFiles(directory, "*.pmx", SearchOption.TopDirectoryOnly));
            // Fallback uppercase/lowercase variants that some filesystems return.
            if (pmxFiles.Count == 0)
            {
                string[] all = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
                foreach (string file in all)
                {
                    if (string.Equals(Path.GetExtension(file), ".pmx", StringComparison.OrdinalIgnoreCase))
                    {
                        pmxFiles.Add(file);
                    }
                }
            }

            foreach (string pmx in pmxFiles)
            {
                _availableModels.Add(new GiantessModelOption
                {
                    displayName = Path.GetFileNameWithoutExtension(pmx),
                    directory = directory,
                    pmxFileName = Path.GetFileName(pmx),
                });
            }

            return _availableModels.Count;
        }

        /// <summary>Imports a model from bytes (frame-budgeted). Returns the built result, or null on failure.</summary>
        public async Task<PMXImportResult> ImportAsync(GiantessModelOption option, Transform parent)
        {
            if (option == null || !option.IsValid)
            {
                Debug.LogError("[GiantCity] Invalid model option.");
                return null;
            }

            byte[] pmxBytes = ReadPmxBytes(option);
            if (pmxBytes == null || pmxBytes.Length == 0)
            {
                Debug.LogError("[GiantCity] Could not read PMX bytes for " + option.displayName);
                return null;
            }

            string textureBaseDir = option.directory;

            // Even when a real external picker supplies files, we still read sibling textures from the
            // same folder by name. If the picker provides its own ReadFile we use it for textures too.
            PMXImportOptions options = new PMXImportOptions
            {
                parent = parent,
                sourceName = option.displayName,
                textureBaseDirectory = textureBaseDir,
                applyRenames = false,
                strictVersion = true,
                createAvatar = false,
                loadTextures = (model, opt) => LoadTexturesFromFolder(model, option),
            };

            try
            {
                UMTFrameBudget budget = new UMTFrameBudget(6.0);

                // Parse asynchronously (frame-budgeted), then build asynchronously so the main thread stays responsive.
                PMXModel model;
                using (MemoryStream stream = new MemoryStream(pmxBytes, false))
                {
                    model = await PMXReader.ReadAsync(budget, stream, options.strictVersion);
                }

                if (model == null)
                {
                    Debug.LogError("[GiantCity] PMX parse returned null for " + option.displayName);
                    return null;
                }

                PMXImportResult result = await PMXImporter.BuildUnityObjectsAsync(budget, model, options);
                SaveLastModel(option);
                return result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[GiantCity] Import failed for " + option.displayName + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Attempts to remember the last chosen model so the app can restore it on next launch.</summary>
        public GiantessModelOption LoadLastModel()
        {
            if (!PlayerPrefs.HasKey(LastModelKey))
            {
                return null;
            }

            string path = PlayerPrefs.GetString(LastModelKey, string.Empty);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string dir = Path.GetDirectoryName(path);
            string file = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file) || !File.Exists(path))
            {
                return null;
            }

            // Re-inventory so we pick up the sibling textures reliably.
            InventoryFromDirectory(dir);
            foreach (GiantessModelOption option in _availableModels)
            {
                if (string.Equals(option.pmxFileName, file, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }
            return null;
        }

        /// <summary>Editor-only convenience to pick a .pmx via the OS file dialog.</summary>
        public GiantessModelOption PickViaEditorDialog()
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("选择女巨人 .pmx", string.Empty, "pmx");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string dir = Path.GetDirectoryName(path);
            string file = Path.GetFileName(path);
            string display = Path.GetFileNameWithoutExtension(path);
            var option = new GiantessModelOption { displayName = display, directory = dir, pmxFileName = file };
            return option;
#else
            return _externalPicker != null ? _externalPicker.PickModel() : null;
#endif
        }

        /// <summary>Prompts the user to pick a model (external picker on device, file dialog in the editor).</summary>
        public GiantessModelOption PickModel()
        {
#if UNITY_EDITOR
            return PickViaEditorDialog();
#else
            if (_externalPicker != null)
            {
                return _externalPicker.PickModel();
            }
            Debug.LogWarning("[GiantCity] No IMobileFilePicker installed on device; use the default folder in persistentDataPath/MMDModels.");
            return null;
#endif
        }

        private byte[] ReadPmxBytes(GiantessModelOption option)
        {
            if (_externalPicker != null && !Directory.Exists(option.directory))
            {
                return _externalPicker.ReadFile(option, option.pmxFileName);
            }
            try
            {
                return File.ReadAllBytes(option.PmxPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        private static void SaveLastModel(GiantessModelOption option)
        {
            if (option != null && option.IsValid)
            {
                PlayerPrefs.SetString(LastModelKey, option.PmxPath);
                PlayerPrefs.Save();
            }
        }

        // ---------------------------------------------------------------------
        //  Texture loading from the model's folder
        // ---------------------------------------------------------------------

        private Texture2D[] LoadTexturesFromFolder(PMXModel model, GiantessModelOption option)
        {
            int count = model.texturePaths != null ? model.texturePaths.Length : 0;
            Texture2D[] textures = new Texture2D[count];
            Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; ++i)
            {
                string rel = model.texturePaths[i].ToString();
                if (string.IsNullOrWhiteSpace(rel))
                {
                    continue;
                }

                Texture2D tex = DecodeSiblingTexture(option, rel, cache);
                if (tex != null)
                {
                    tex.name = Path.GetFileNameWithoutExtension(rel) + "_" + i;
                    textures[i] = tex;
                }
            }
            return textures;
        }

        private Texture2D DecodeSiblingTexture(GiantessModelOption option, string relativePath, Dictionary<string, Texture2D> cache)
        {
            string fileName = Path.GetFileName(relativePath);
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            if (cache.TryGetValue(fileName, out Texture2D cached))
            {
                return cached;
            }

            byte[] bytes = ReadSiblingBytes(option, fileName);
            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogWarning("[GiantCity] Missing texture: " + fileName);
                return null;
            }

            Texture2D decoded = DecodeTexture(fileName, bytes);
            if (decoded == null)
            {
                Debug.LogWarning("[GiantCity] Could not decode texture: " + fileName);
                return null;
            }

            cache[fileName] = decoded;
            return decoded;
        }

        private byte[] ReadSiblingBytes(GiantessModelOption option, string fileName)
        {
            if (_externalPicker != null && !Directory.Exists(option.directory))
            {
                return _externalPicker.ReadFile(option, fileName);
            }
            try
            {
                string path = Path.Combine(option.directory, fileName);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        private static Texture2D DecodeTexture(string fileName, byte[] bytes)
        {
            string ext = Path.GetExtension(fileName);
            try
            {
                if (string.Equals(ext, ".tga", StringComparison.OrdinalIgnoreCase))
                {
                    using (MemoryStream stream = new MemoryStream(bytes, false))
                    {
                        return UMT.ThirdParty.TGALoader.LoadTGA(stream);
                    }
                }

                if (string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    B83.Image.BMP.BMPImage bmp = new B83.Image.BMP.BMPLoader().LoadBMP(bytes);
                    return bmp != null ? bmp.ToTexture2D() : null;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, bytes))
                {
                    return texture;
                }
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }
    }
}
