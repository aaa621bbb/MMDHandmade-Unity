using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMDPlayer
{
    /// <summary>
    /// uGUI playback panel. Pure view: it binds to the <see cref="MMDPlayerController"/> and forwards
    /// user input; all state/behaviour lives in the controller.
    ///
    /// The scene only needs a Canvas (with this component) plus an EventSystem. This script builds its
    /// own child controls at runtime so no hand-authored prefab/references are required.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class MMDPlaybackUI : MonoBehaviour
    {
        private MMDPlayerController _controller;

        private Text _statusText;
        private Text _timeText;
        private Button _playButton;
        private Button _pauseButton;
        private Button _stopButton;
        private Button _resetPoseButton;
        private Toggle _loopToggle;
        private Toggle _physicsToggle;
        private Text _physicsHint;
        private Slider _progressSlider;
        private RectTransform _morphRow;

        private OptionDropdown _modelDropdown;
        private OptionDropdown _motionDropdown;
        private OptionDropdown _morphDropdown;

        private bool _draggingProgress;
        private bool _syncingSlider;
        private bool _subscribed;

        private void Awake()
        {
            EnsureEventSystem();
            EnsureCanvasLayout();

            if (_progressSlider == null)
            {
                BuildUI();
            }

            ResolveController();
        }

        private void Start()
        {
            ResolveController();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            // Reset the "dragging" flag when the pointer is released so the display resumes tracking.
            if (_draggingProgress && Input.GetMouseButtonUp(0))
            {
                _draggingProgress = false;
            }

            if (_controller == null || _draggingProgress)
            {
                return;
            }

            RefreshProgress();
        }

        // ---------------------------------------------------------------------
        //  Wiring
        // ---------------------------------------------------------------------

        private void ResolveController()
        {
            MMDPlayerController found = FindObjectOfType<MMDPlayerController>();
            if (found != _controller)
            {
                Unsubscribe();
                _controller = found;
                Subscribe();
            }

            PopulateModelDropdown();
            PopulateMotionDropdown();
            PopulateMorphDropdown();
            RefreshPhysicsToggle();
            RefreshPlaybackControls();
            RefreshHeader();
        }

        private void Subscribe()
        {
            if (_controller == null || _subscribed)
            {
                return;
            }

            _controller.OnModelLoaded += HandleModelLoaded;
            _controller.OnClipChanged += HandleClipChanged;
            _controller.OnError += HandleError;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_controller != null && _subscribed)
            {
                _controller.OnModelLoaded -= HandleModelLoaded;
                _controller.OnClipChanged -= HandleClipChanged;
                _controller.OnError -= HandleError;
            }
            _subscribed = false;
        }

        private void HandleModelLoaded()
        {
            PopulateModelDropdown();
            PopulateMotionDropdown();
            PopulateMorphDropdown();
            RefreshPhysicsToggle();
            RefreshPlaybackControls();
            RefreshHeader();
        }

        private void HandleClipChanged(string clipName)
        {
            RefreshPlaybackControls();
            RefreshHeader();
        }

        private void HandleError(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }

        // ---------------------------------------------------------------------
        //  Data binding
        // ---------------------------------------------------------------------

        private void PopulateModelDropdown()
        {
            if (_modelDropdown == null || _controller == null)
            {
                return;
            }

            List<MMDAssetLibrary.ModelEntry> entries = new List<MMDAssetLibrary.ModelEntry>();
            List<string> names = new List<string>();
            if (_controller.library != null)
            {
                foreach (MMDAssetLibrary.ModelEntry entry in _controller.library.entries)
                {
                    if (entry == null || entry.model == null)
                    {
                        continue;
                    }
                    entries.Add(entry);
                    names.Add(string.IsNullOrWhiteSpace(entry.displayName) ? entry.model.name : entry.displayName);
                }
            }

            _modelDropdown.SetOptions(names, (index) =>
            {
                if (entries.Count == 0 || index < 0 || index >= entries.Count)
                {
                    return;
                }
                _controller.LoadModel(entries[index].model);
            });
        }

        private void PopulateMotionDropdown()
        {
            if (_motionDropdown == null || _controller == null)
            {
                return;
            }

            IReadOnlyList<AnimationClip> motions = _controller.CurrentMotions;
            List<string> names = new List<string>();
            if (motions != null)
            {
                for (int i = 0; i < motions.Count; ++i)
                {
                    names.Add(motions[i] != null ? motions[i].name : string.Format("Motion {0}", i));
                }
            }

            _motionDropdown.SetOptions(names, (index) =>
            {
                if (motions == null || motions.Count == 0 || index < 0 || index >= motions.Count)
                {
                    return;
                }
                _controller.PlayMotion(motions[index]);
            });
        }

        private void PopulateMorphDropdown()
        {
            if (_morphDropdown == null || _controller == null)
            {
                return;
            }

            List<string> names = _controller.GetMorphNames();
            if (_morphRow != null)
            {
                _morphRow.gameObject.SetActive(_controller.CurrentModel != null && names.Count > 0);
            }

            _morphDropdown.SetOptions(names, (index) =>
            {
                if (names.Count == 0 || index < 0 || index >= names.Count)
                {
                    return;
                }
                _controller.SetMorph(names[index], 1f);
            });
        }

        private void RefreshHeader()
        {
            if (_statusText == null)
            {
                return;
            }

            if (_controller == null)
            {
                _statusText.text = "尚未找到 MMDPlayerController。";
                return;
            }

            if (_controller.CurrentModel == null)
            {
                _statusText.text =
                    "请把 .pmx 放入 Assets/MMDResources/Models、把 .vmd 放入 Assets/MMDResources/Motions，然后点扫描器的 Refresh。";
                return;
            }

            if (_controller.PhysicsAvailable == false)
            {
                _statusText.text = string.Format("当前模型：{0}　|　{1}", _controller.CurrentModel.name, MMDPhysicsGuard.UnsupportedMessage);
            }
            else
            {
                _statusText.text = string.Format("当前模型：{0}", _controller.CurrentModel.name);
            }
        }

        private void RefreshPhysicsToggle()
        {
            if (_physicsToggle == null)
            {
                return;
            }

            bool available = _controller != null && _controller.PhysicsAvailable;
            _physicsToggle.interactable = available;

            if (!available && _controller != null)
            {
                // The controller already disables physics when unavailable; keep the toggle in sync.
                _controller.livePhysics = false;
            }

            if (_physicsHint != null)
            {
                _physicsHint.text = available ? "物理" : "物理（本平台不可用）";
            }
        }

        private void RefreshPlaybackControls()
        {
            if (_controller == null)
            {
                return;
            }

            bool hasClip = _controller.CurrentClip != null;
            bool hasModel = _controller.CurrentModel != null;

            if (_progressSlider != null)
            {
                _progressSlider.interactable = hasClip;
            }
            if (_playButton != null)
            {
                _playButton.interactable = hasClip;
            }
            if (_pauseButton != null)
            {
                _pauseButton.interactable = hasClip;
            }
            if (_stopButton != null)
            {
                _stopButton.interactable = hasModel;
            }
            if (_resetPoseButton != null)
            {
                _resetPoseButton.interactable = hasModel;
            }
            if (_loopToggle != null && _controller.loop != _loopToggle.isOn)
            {
                _loopToggle.SetIsOnWithoutNotify(_controller.loop);
            }
            if (_physicsToggle != null)
            {
                // Keep the toggle in sync with the controller's effective physics state.
                _physicsToggle.SetIsOnWithoutNotify(_controller.IsPhysicsEnabled);
            }

            RefreshProgress();
        }

        private void RefreshProgress()
        {
            if (_controller == null || _progressSlider == null || _timeText == null)
            {
                return;
            }

            float duration = _controller.CurrentDuration;
            float time = _controller.CurrentTime;

            if (!_draggingProgress)
            {
                float value = duration > 0.0001f ? Mathf.Clamp01(time / duration) : 0f;
                _syncingSlider = true;
                _progressSlider.value = value;
                _syncingSlider = false;
                _timeText.text = string.Format("{0} / {1}", FormatTime(time), FormatTime(duration));
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return string.Format("{0:00}:{1:00}", total / 60, total % 60);
        }

        private void OnProgressChanged(float value)
        {
            // Ignore programmatic value writes (single-threaded guard).
            if (_controller == null || _syncingSlider)
            {
                return;
            }

            _draggingProgress = true;
            float duration = _controller.CurrentDuration;
            float t = value * duration;
            _controller.SetTime(t);

            if (_timeText != null)
            {
                _timeText.text = string.Format("{0} / {1}", FormatTime(t), FormatTime(duration));
            }
        }

        // ---------------------------------------------------------------------
        //  Scene bootstrap helpers
        // ---------------------------------------------------------------------

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }

        private void EnsureCanvasLayout()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            if (GetComponent<CanvasScaler>() == null)
            {
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.matchWidthOrHeight = 0f;
            }
        }

        // ---------------------------------------------------------------------
        //  Panel construction
        // ---------------------------------------------------------------------

        private void BuildUI()
        {
            RectTransform canvasRT = transform as RectTransform;

            // Bottom overlay panel.
            GameObject panelGO = UIHelper.CreateRect("PlaybackPanel", transform);
            RectTransform panelRT = panelGO.transform as RectTransform;
            panelRT.anchorMin = new Vector2(0f, 0f);
            panelRT.anchorMax = new Vector2(1f, 0f);
            panelRT.pivot = new Vector2(0.5f, 0f);
            panelRT.offsetMin = new Vector2(0f, 0f);
            panelRT.offsetMax = new Vector2(0f, 312f);

            Image panelImage = panelGO.AddComponent<Image>();
            panelImage.color = new Color(0.05f, 0.05f, 0.08f, 0.78f);

            VerticalLayoutGroup vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 8, 8);
            vlg.spacing = 8f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            // Status / guidance.
            _statusText = UIHelper.CreateText(panelRT, "Status", string.Empty, 14, TextAnchor.MiddleLeft);
            _statusText.color = new Color(1f, 0.9f, 0.6f, 1f);
            UIHelper.MakePreferredHeight(_statusText.rectTransform, 24f);

            // Model.
            RectTransform modelRow = BuildRow(panelRT, new[] { ("模型", 60f), (null, 560f) });
            _modelDropdown = OptionDropdown.Create(modelRow, "ModelDropdown", 400f);

            // Motion.
            RectTransform motionRow = BuildRow(panelRT, new[] { ("动作", 60f), (null, 560f) });
            _motionDropdown = OptionDropdown.Create(motionRow, "MotionDropdown", 400f);

            // Transport.
            RectTransform transportRow = BuildRow(panelRT, new[] { ("控制", 60f), (null, 560f) });
            _playButton = UIHelper.CreateButton(transportRow, "Play", "播放", 88f, () => _controller?.Resume());
            _pauseButton = UIHelper.CreateButton(transportRow, "Pause", "暂停", 88f, () => _controller?.Pause());
            _stopButton = UIHelper.CreateButton(transportRow, "Stop", "停止", 88f, () => _controller?.StopAndReset());
            _resetPoseButton = UIHelper.CreateButton(transportRow, "ResetPose", "重置姿势", 104f, () => _controller?.StopAndReset());
            _loopToggle = UIHelper.CreateToggle(transportRow, "Loop", "循环", _controller != null && _controller.loop, (v) => _controller?.SetLoop(v));

            // Progress.
            RectTransform progressRow = BuildRow(panelRT, new[] { ("进度", 60f), (null, 560f) });
            _progressSlider = UIHelper.CreateSlider(progressRow, "Progress", 0f, 1f, OnProgressChanged);
            _progressSlider.interactable = false;
            _timeText = UIHelper.CreateText(progressRow, "Time", "00:00 / 00:00", 13, TextAnchor.MiddleLeft);
            UIHelper.MakeFixedWidth(_timeText.rectTransform, 130f);

            // Physics.
            RectTransform physicsRow = BuildRow(panelRT, new[] { ("物理", 60f), (null, 560f) });
            _physicsToggle = UIHelper.CreateToggle(physicsRow, "Physics", "物理", true, (v) => _controller?.TogglePhysics(v));
            _physicsHint = UIHelper.CreateText(physicsRow, "PhysicsHint", string.Empty, 13, TextAnchor.MiddleLeft);

            // Morph.
            RectTransform morphRow = BuildRow(panelRT, new[] { ("表情", 60f), (null, 560f) });
            _morphRow = morphRow;
            _morphDropdown = OptionDropdown.Create(morphRow, "MorphDropdown", 400f);
            _morphRow.gameObject.SetActive(false);
        }

        /// <summary>
        /// Builds a single horizontal row: an optional label on the left plus a fixed-width control area
        /// on the right. Returns the control area (the right-most element) that dropdowns/controls are
        /// added into.
        /// </summary>
        private RectTransform BuildRow(RectTransform parent, params (string label, float controlWidth)[] cells)
        {
            GameObject row = UIHelper.CreateRect("Row", parent);
            RectTransform rt = row.transform as RectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 28f);
            UIHelper.MakePreferredHeight(rt, 28f);

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(0, 0, 0, 0);

            RectTransform lastControl = null;
            foreach ((string label, float controlWidth) cell in cells)
            {
                if (cell.label != null)
                {
                    Text labelText = UIHelper.CreateText(rt, "Label", cell.label, 14, TextAnchor.MiddleLeft);
                    labelText.color = new Color(0.85f, 0.85f, 0.9f, 1f);
                    UIHelper.MakeFixedWidth(labelText.rectTransform, cell.controlWidth);
                    LayoutElement le = labelText.gameObject.AddComponent<LayoutElement>();
                    le.minWidth = cell.controlWidth;
                    le.preferredWidth = cell.controlWidth;
                }
                else
                {
                    GameObject control = UIHelper.CreateRect("Control", rt);
                    RectTransform controlRT = control.transform as RectTransform;
                    UIHelper.MakeFixedWidth(controlRT, cell.controlWidth);
                    LayoutElement le = control.gameObject.AddComponent<LayoutElement>();
                    le.minWidth = cell.controlWidth;
                    le.preferredWidth = cell.controlWidth;
                    le.flexibleWidth = 0f;

                    // Lay out the controls inside this area side by side.
                    HorizontalLayoutGroup controlHlg = control.AddComponent<HorizontalLayoutGroup>();
                    controlHlg.spacing = 6f;
                    controlHlg.childControlWidth = true;
                    controlHlg.childControlHeight = true;
                    controlHlg.childForceExpandWidth = false;
                    controlHlg.childForceExpandHeight = true;
                    controlHlg.padding = new RectOffset(0, 0, 0, 0);

                    lastControl = controlRT;
                }
            }

            return lastControl;
        }

        // ---------------------------------------------------------------------
        //  Minimal self-contained dropdown
        // ---------------------------------------------------------------------

        /// <summary>
        /// A reliable, fully code-built dropdown: a trigger button showing the current selection that,
        /// when clicked, toggles a popup list of option buttons beneath it. The popup is parented to the
        /// canvas so it overlays the panel without disturbing the layout.
        /// </summary>
        private sealed class OptionDropdown
        {
            private readonly RectTransform _root;
            private readonly Text _label;
            private RectTransform _popup;
            private readonly float _width;
            private readonly List<Button> _buttons = new List<Button>();
            private readonly List<string> _options = new List<string>();
            private Action<int> _onSelect;
            private bool _open;
            private int _current = -1;

            private OptionDropdown(RectTransform root, Text label, float width)
            {
                _root = root;
                _label = label;
                _width = width;
            }

            public static OptionDropdown Create(RectTransform parent, string name, float width)
            {
                GameObject trigger = UIHelper.CreateRect(name, parent);
                RectTransform triggerRT = trigger.transform as RectTransform;
                triggerRT.anchorMin = new Vector2(0f, 0f);
                triggerRT.anchorMax = new Vector2(1f, 0f);
                triggerRT.pivot = new Vector2(0.5f, 0.5f);

                Image bg = trigger.AddComponent<Image>();
                bg.color = new Color(0.25f, 0.27f, 0.36f, 1f);

                Button button = trigger.AddComponent<Button>();
                button.targetGraphic = bg;

                Text label = UIHelper.CreateText(triggerRT, "Label", "（无）", 13, TextAnchor.MiddleLeft);
                label.color = Color.white;

                LayoutElement layout = trigger.AddComponent<LayoutElement>();
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
                layout.minHeight = 24f;
                layout.preferredHeight = 24f;

                OptionDropdown dropdown = new OptionDropdown(triggerRT, label, width);

                button.onClick.AddListener(dropdown.Toggle);
                return dropdown;
            }

            public void SetOptions(IList<string> options, Action<int> onSelect)
            {
                _onSelect = onSelect;
                _options.Clear();
                if (options != null)
                {
                    _options.AddRange(options);
                }

                if (_options.Count == 0)
                {
                    _label.text = "（无）";
                    _current = -1;
                    Close();
                    return;
                }

                _current = Mathf.Clamp(_current, 0, _options.Count - 1);
                if (_current < 0)
                {
                    _current = 0;
                }
                _label.text = _options[_current];
                RebuildPopup();
            }

            private void Toggle()
            {
                if (_options.Count == 0)
                {
                    return;
                }

                if (_open)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }

            private void Open()
            {
                RebuildPopup();
                PositionPopup();
                _popup.gameObject.SetActive(true);
                _popup.SetAsLastSibling();
                _open = true;
            }

            private void Close()
            {
                if (_popup != null && _popup.gameObject != null)
                {
                    _popup.gameObject.SetActive(false);
                }
                _open = false;
            }

            private void RebuildPopup()
            {
                RectTransform popupContainer = EnsurePopup();
                foreach (Button b in _buttons)
                {
                    if (b != null)
                    {
                        UnityEngine.Object.Destroy(b.gameObject);
                    }
                }
                _buttons.Clear();

                for (int i = 0; i < _options.Count; ++i)
                {
                    int captured = i;
                    string text = _options[i];
                    Button optionButton = UIHelper.CreateOptionButton(popupContainer, string.Format("Option_{0}", i), text, () =>
                    {
                        _current = captured;
                        _label.text = text;
                        Close();
                        _onSelect?.Invoke(captured);
                    });
                    _buttons.Add(optionButton);
                }
            }

            private RectTransform EnsurePopup()
            {
                if (_popup != null)
                {
                    return _popup;
                }

                // Popup is parented to the canvas frame so it renders on top and never reflows the layout.
                RectTransform canvasRT = _root.parent as RectTransform;
                while (canvasRT != null && canvasRT.GetComponent<Canvas>() == null)
                {
                    canvasRT = canvasRT.parent as RectTransform;
                }
                if (canvasRT == null)
                {
                    canvasRT = _root.parent as RectTransform;
                }

                GameObject popupGO = UIHelper.CreateRect("DropdownPopup", canvasRT);
                RectTransform popupRT = popupGO.transform as RectTransform;
                popupRT.anchorMin = Vector2.zero;
                popupRT.anchorMax = Vector2.zero;
                // Bottom-left pivot so the popup grows upward from the anchor point (the panel is
                // bottom-anchored, so a downward-opening popup would run off the bottom of the screen).
                popupRT.pivot = new Vector2(0f, 0f);
                popupRT.sizeDelta = new Vector2(_width, 0f);

                Image bg = popupGO.AddComponent<Image>();
                bg.color = new Color(0.15f, 0.16f, 0.22f, 0.98f);

                VerticalLayoutGroup vlg = popupGO.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(4, 4, 4, 4);
                vlg.spacing = 2f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                ContentSizeFitter fitter = popupGO.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                popupGO.SetActive(false);

                _popup = popupRT;
                return popupRT;
            }

            private void PositionPopup()
            {
                if (_popup == null)
                {
                    return;
                }

                // Anchor the popup's bottom-left corner to the trigger's top-left corner so it opens
                // upward (above the trigger), which stays on screen for a bottom-anchored panel.
                RectTransform canvasRT = _popup.parent as RectTransform;

                Vector3[] corners = new Vector3[4];
                _root.GetWorldCorners(corners); // [0]=bottomLeft [1]=topLeft [2]=topRight [3]=bottomRight

                Vector2 topLeftLocal;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, corners[1], null, out topLeftLocal);

                _popup.anchoredPosition = new Vector2(topLeftLocal.x, topLeftLocal.y);
                _popup.sizeDelta = new Vector2(_width, 0f);
            }
        }

        // ---------------------------------------------------------------------
        //  uGUI construction helpers
        // ---------------------------------------------------------------------

        private static class UIHelper
        {
            public static GameObject CreateRect(string name, Transform parent)
            {
                GameObject go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                return go;
            }

            public static Text CreateText(RectTransform parent, string name, string text, int fontSize, TextAnchor anchor)
            {
                GameObject go = CreateRect(name, parent);
                RectTransform rt = go.transform as RectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                Text txt = go.AddComponent<Text>();
                txt.text = text;
                txt.fontSize = fontSize;
                txt.alignment = anchor;
                txt.color = Color.white;
                txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                return txt;
            }

            public static void MakePreferredHeight(RectTransform rt, float height)
            {
                LayoutElement le = rt.GetComponent<LayoutElement>();
                if (le == null)
                {
                    le = rt.gameObject.AddComponent<LayoutElement>();
                }
                le.minHeight = height;
                le.preferredHeight = height;
            }

            public static void MakeFixedWidth(RectTransform rt, float width)
            {
                LayoutElement le = rt.GetComponent<LayoutElement>();
                if (le == null)
                {
                    le = rt.gameObject.AddComponent<LayoutElement>();
                }
                le.minWidth = width;
                le.preferredWidth = width;
                le.flexibleWidth = 0f;
            }

            public static Button CreateButton(RectTransform parent, string name, string label, float width, Action onClick)
            {
                GameObject go = CreateRect(name, parent);
                RectTransform rt = go.transform as RectTransform;

                Image bg = go.AddComponent<Image>();
                bg.color = new Color(0.3f, 0.32f, 0.4f, 1f);

                Button button = go.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() => onClick?.Invoke());

                Text txt = CreateText(rt, "Label", label, 14, TextAnchor.MiddleCenter);
                txt.color = Color.white;
                MakeFixedWidth(rt, width);
                MakePreferredHeight(rt, 24f);
                return button;
            }

            public static Button CreateOptionButton(RectTransform popup, string name, string label, Action onClick)
            {
                GameObject go = CreateRect(name, popup);
                RectTransform rt = go.transform as RectTransform;

                Image bg = go.AddComponent<Image>();
                bg.color = new Color(0.22f, 0.24f, 0.32f, 1f);

                Button button = go.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() => onClick?.Invoke());

                Text txt = CreateText(rt, "Label", label, 14, TextAnchor.MiddleLeft);
                txt.color = Color.white;

                MakePreferredHeight(rt, 24f);
                return button;
            }

            public static Toggle CreateToggle(RectTransform parent, string name, string label, bool initial, Action<bool> onChanged)
            {
                GameObject go = CreateRect(name, parent);
                RectTransform rt = go.transform as RectTransform;

                Image bg = go.AddComponent<Image>();
                bg.color = new Color(0.28f, 0.3f, 0.38f, 1f);

                Toggle toggle = go.AddComponent<Toggle>();
                toggle.targetGraphic = bg;
                toggle.isOn = initial;
                toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));

                Text txt = CreateText(rt, "Label", label, 14, TextAnchor.MiddleLeft);
                txt.color = Color.white;

                MakeFixedWidth(rt, 78f);
                MakePreferredHeight(rt, 24f);
                return toggle;
            }

            public static Slider CreateSlider(RectTransform parent, string name, float min, float max, Action<float> onChanged)
            {
                GameObject go = CreateRect(name, parent);
                RectTransform rt = go.transform as RectTransform;

                Image bg = go.AddComponent<Image>();
                bg.color = new Color(0.2f, 0.2f, 0.26f, 1f);

                // Fill area + fill.
                RectTransform fillAreaRT = CreateArea(rt, "Fill Area", 4f);
                GameObject fill = CreateRect("Fill", fillAreaRT);
                RectTransform fillRT = fill.transform as RectTransform;
                fillRT.anchorMin = new Vector2(0f, 0f);
                fillRT.anchorMax = new Vector2(0f, 1f);
                fillRT.pivot = new Vector2(0f, 0.5f);
                fillRT.sizeDelta = new Vector2(6f, 0f);
                Image fillImage = fill.AddComponent<Image>();
                fillImage.color = new Color(0.35f, 0.6f, 1f, 1f);

                // Handle slide area + handle.
                RectTransform handleAreaRT = CreateArea(rt, "Handle Slide Area", 0f);
                GameObject handle = CreateRect("Handle", handleAreaRT);
                RectTransform handleRT = handle.transform as RectTransform;
                handleRT.anchorMin = new Vector2(0f, 0f);
                handleRT.anchorMax = new Vector2(0f, 1f);
                handleRT.pivot = new Vector2(0.5f, 0.5f);
                handleRT.sizeDelta = new Vector2(18f, 0f);
                Image handleImage = handle.AddComponent<Image>();
                handleImage.color = Color.white;

                Slider slider = go.AddComponent<Slider>();
                slider.minValue = min;
                slider.maxValue = max;
                slider.direction = Slider.Direction.LeftToRight;
                slider.fillRect = fillRT;
                slider.handleRect = handleRT;
                slider.targetGraphic = handleImage;
                slider.onValueChanged.AddListener(v => onChanged?.Invoke(v));

                MakePreferredHeight(rt, 24f);
                LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.minWidth = 120f;
                return slider;
            }

            private static RectTransform CreateArea(RectTransform parent, string name, float inset)
            {
                GameObject go = CreateRect(name, parent);
                RectTransform rt = go.transform as RectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = new Vector2(inset, inset);
                rt.offsetMax = new Vector2(-inset, -inset);
                return rt;
            }
        }
    }
}
