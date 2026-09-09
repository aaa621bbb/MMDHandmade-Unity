using UnityEngine;
using UnityEngine.UI;

namespace GiantCity
{
    /// <summary>
    /// Minimal uGUI HUD for the giantess-city sandbox. Pure view: it forwards toggle/button taps to the
    /// <see cref="GiantCityController"/> and reflects the invincible state / status message. Built entirely
    /// in code so the scene just needs a Canvas + this component.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class GiantCityHUD : MonoBehaviour
    {
        private Text _statusText;
        private Toggle _invincibleToggle;
        private GiantCityController _controller;

        private void Awake()
        {
            _controller = FindObjectOfType<GiantCityController>();

            Canvas canvas = GetComponent<Canvas>();
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            BuildUI();
        }

        /// <summary>Shows a status line (e.g. import progress / error).</summary>
        public void SetStatus(string message)
        {
            _statusText.text = message ?? string.Empty;
        }

        /// <summary>Sets the invincible toggle without firing its callback during reflection.</summary>
        public void SetInvincible(bool on)
        {
            if (_invincibleToggle != null)
            {
                _invincibleToggle.SetIsOnWithoutNotify(on);
            }
        }

        private void BuildUI()
        {
            RectTransform canvasRT = transform as RectTransform;

            // Status (top-left).
            _statusText = CreateText(canvasRT, "Status", "请选择女巨人", 20, TextAnchor.MiddleLeft);
            RectTransform statusRT = _statusText.rectTransform;
            statusRT.anchorMin = new Vector2(0f, 1f);
            statusRT.anchorMax = new Vector2(0f, 1f);
            statusRT.pivot = new Vector2(0f, 1f);
            statusRT.anchoredPosition = new Vector2(24f, -24f);
            statusRT.sizeDelta = new Vector2(700f, 40f);
            _statusText.color = Color.white;

            // Invincible toggle (top-right).
            Toggle toggle = CreateToggle(canvasRT, "Invincible", "无敌", false);
            toggle.onValueChanged.AddListener((v) => _controller?.SetInvincible(v));

            // Pick model button (below toggle).
            Button pick = CreateButton(canvasRT, "Pick", "换模型", 130f, 50f, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -84f), new Color(0.22f, 0.32f, 0.48f, 1f));
            pick.onClick.AddListener(() => _controller?.PickGiantessFromDevice());

            // Respawn button (bottom-left).
            Button respawn = CreateButton(canvasRT, "Respawn", "重生", 130f, 50f, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Color(0.3f, 0.24f, 0.4f, 1f));
            respawn.onClick.AddListener(() => _controller?.RespawnPlayer());
        }

        private static Text CreateText(RectTransform parent, string name, string text, int fontSize, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
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

        private static Toggle CreateToggle(RectTransform parent, string name, string label, bool initial)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -24f);
            rt.sizeDelta = new Vector2(160f, 44f);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.22f, 0.32f, 0.48f, 1f);

            Toggle toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.isOn = initial;

            Text txt = CreateText(rt, "Label", label, 18, TextAnchor.MiddleCenter);
            txt.color = Color.white;
            return toggle;
        }

        private static Button CreateButton(RectTransform parent, string name, string label,
            float width, float height, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = new Vector2(width, height);

            Image bg = go.AddComponent<Image>();
            bg.color = color;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = bg;

            Text txt = CreateText(rt, "Label", label, 18, TextAnchor.MiddleCenter);
            txt.color = Color.white;
            return button;
        }
    }
}
