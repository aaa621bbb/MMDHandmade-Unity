using UnityEngine;
using UnityEngine.UI;

namespace MMDGiantGame
{
    /// <summary>
    /// Minimal uGUI HUD for the giant stomp game. Pure view: it forward button taps to the
    /// <see cref="MMDGiantGameManager"/> and reflects the score. Built entirely in code (no prefab
    /// references), so the scene just needs a Canvas + EventSystem plus this component.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class MMDGiantHUD : MonoBehaviour
    {
        private Text _scoreText;
        private Text _hintText;
        private MMDGiantGameManager _manager;

        private void Awake()
        {
            _manager = FindObjectOfType<MMDGiantGameManager>();
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

        /// <summary>Updates the on-screen score label.</summary>
        public void UpdateScore(int score)
        {
            if (_scoreText != null)
            {
                _scoreText.text = "得分  " + score;
            }
        }

        private void BuildUI()
        {
            RectTransform canvasRT = transform as RectTransform;

            // Score (top-left).
            _scoreText = CreateText(canvasRT, "Score", "得分  0", 34, TextAnchor.MiddleLeft);
            RectTransform scoreRT = _scoreText.rectTransform;
            scoreRT.anchorMin = new Vector2(0f, 1f);
            scoreRT.anchorMax = new Vector2(0f, 1f);
            scoreRT.pivot = new Vector2(0f, 1f);
            scoreRT.anchoredPosition = new Vector2(24f, -20f);
            scoreRT.sizeDelta = new Vector2(300f, 48f);
            _scoreText.color = Color.white;

            // Hint (top, under score).
            _hintText = CreateText(canvasRT, "Hint",
                "单指拖动：移动　　轻点 / 按钮：踩踏", 16, TextAnchor.MiddleLeft);
            RectTransform hintRT = _hintText.rectTransform;
            hintRT.anchorMin = new Vector2(0f, 1f);
            hintRT.anchorMax = new Vector2(0f, 1f);
            hintRT.pivot = new Vector2(0f, 1f);
            hintRT.anchoredPosition = new Vector2(24f, -76f);
            hintRT.sizeDelta = new Vector2(500f, 30f);
            _hintText.color = new Color(1f, 1f, 1f, 0.85f);

            // Stomp button (bottom-right, large).
            Button stomp = CreateButton(canvasRT, "Stomp", "踩踏", 160f, 160f, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 24f), Color.red);
            stomp.onClick.AddListener(() => _manager?.DoStomp());

            // Restart button (bottom-left).
            Button restart = CreateButton(canvasRT, "Restart", "重新开始", 150f, 52f, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 24f), new Color(0.2f, 0.3f, 0.5f, 1f));
            restart.onClick.AddListener(() => _manager?.ResetGame());
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

            Text txt = CreateText(rt, "Label", label, 22, TextAnchor.MiddleCenter);
            txt.color = Color.white;
            return button;
        }
    }
}
