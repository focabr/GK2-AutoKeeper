using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AutoKeeper.Bot;
using AutoKeeper.Config;
using AutoKeeper.Core;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoKeeper.UI
{
    /// <summary>
    /// Status panel (F9) with the GAME'S LOOK (0.3.38, user's request after seeing a mod whose menu looks like the game):
    /// the frame and title plate of the game's Settings window, its text style (the game's own pixel font, per language)
    /// and its dialog buttons, all cloned at runtime from the game's UI — no copied art, no code from other mods.
    ///
    /// Not a LazyWindow: it does not pause the game or take the keyboard; it is a plain canvas under the game's UI root,
    /// below the F11 window. Blocks: title plate · "Status" (state, reason, task, place, energy, sleep…) · "Session" ·
    /// "Events" (game clock + message, newest at the bottom) · 2×2 buttons (bot on/off, settings, diagnostic, close).
    /// Texts are rebuilt at most 4× per second and the layout only when a text changed.
    /// Documented exception to the "only GameApi touches the game" rule (same as <see cref="NativeSettingsWindow"/>);
    /// if any part is missing, <see cref="TryCreate"/> fails and the plugin draws the simple IMGUI panel instead.
    /// </summary>
    internal sealed class NativeStatusPanel : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;
        // Width in the panel's own units (text = the game's 16-unit font). "Small" draws the panel at half the game's
        // pixel size (1080p: canvas ×2, panel ×0.5 → 1 px per art pixel), "Large" at the game's own size.
        private const float WidthSmall = 460f;
        private const float WidthLarge = 320f;
        private const float Margin = 6f;
        private const float Gap = 3f;

        private static readonly FieldInfo SwitchHeaderField = AccessTools.Field(typeof(UISwitchButton), "headerLabel");

        private Settings settings;
        private BotController bot;
        private Action onSettings;
        private Action onDump;

        private RectTransform rootRt;
        private RectTransform headerRt;
        private Transform staging;
        private GameObject labelTemplate;
        private GameObject buttonTemplate;
        private TextMeshProUGUI title;
        private Section status;
        private Section session;
        private Section events;
        private readonly List<UIDialogWindowButton> buttons = new List<UIDialogWindowButton>();
        private readonly List<TextMeshProUGUI> texts = new List<TextMeshProUGUI>();

        // Paddings from the frame's inner background ("BackMask", inset 13 units on each side): the frame sprite's
        // 9-slice border is far wider than the visible edge (first print: 88-unit margins squeezed the text into a
        // 72-unit column); 11 units (second print) put the text 2 units over the border.
        private static float SidePad = 19f;
        private float bottomPad = 10f;
        private float topPad = 12f;
        private float nextRefresh;
        private bool layoutDirty = true;
        private bool lastPt;
        private bool lastBotOn;
        private bool buttonsDrawn;
        private OverlayCorner lastCorner = (OverlayCorner)(-1);
        private float panelWidth = WidthSmall;
        private float lastScale = -1f;

        private sealed class Section
        {
            public TextMeshProUGUI Header;
            public RectTransform Line;
            public TextMeshProUGUI Body;
            public bool Visible;
        }

        /// <summary>Screen area of the panel in GUI coordinates (y down), for the click guard; zero when hidden.</summary>
        public Rect GuiRect { get; private set; }

        /// <summary>An error while running: the plugin removes this panel and goes back to the simple one.</summary>
        public bool Failed { get; private set; }

        // ------------------------------------------------------------------ creation

        internal static NativeStatusPanel TryCreate(Settings settings, BotController bot, Action onSettings, Action onDump, out string error, out bool retry)
        {
            GameObject root = null;
            retry = false;
            try
            {
                UIGameSettingsWindow src = LazyUI.GetWindow<UIGameSettingsWindow>();
                if (src == null)
                {
                    error = Lang.T("janela de configurações do jogo indisponível", "game settings window unavailable");
                    retry = true; // the game builds its windows a little after startup
                    return null;
                }
                Transform srcFrame = src.transform.Find("GenericWIndowLayout/Frame");
                var srcSwitch = AccessTools.Field(typeof(UIGameSettingsWindow), "fullscreenButton")?.GetValue(src) as UISwitchButton;
                var srcButton = AccessTools.Field(typeof(UIGameSettingsWindow), "lazyButton")?.GetValue(src) as UIDialogWindowButton;
                var srcLabel = srcSwitch != null ? SwitchHeaderField?.GetValue(srcSwitch) as TextMeshProUGUI : null;
                if (srcFrame == null || srcLabel == null || srcButton == null)
                {
                    error = Lang.T($"peças não encontradas (moldura={srcFrame != null}, texto={srcLabel != null}, botão={srcButton != null})",
                        $"parts not found (frame={srcFrame != null}, text={srcLabel != null}, button={srcButton != null})");
                    return null;
                }

                root = new GameObject("AutoKeeperStatusPanel", typeof(RectTransform));
                root.transform.SetParent(NativeSettingsWindow.FindUiRoot(src), false);
                Canvas canvas = root.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 300; // under the F11 window (460)
                root.AddComponent<GraphicRaycaster>();

                var stagingGo = new GameObject("AK_Templates", typeof(RectTransform));
                stagingGo.transform.SetParent(root.transform, false);
                stagingGo.SetActive(false);

                NativeStatusPanel p = root.AddComponent<NativeStatusPanel>();
                p.settings = settings;
                p.bot = bot;
                p.onSettings = onSettings;
                p.onDump = onDump;
                p.rootRt = (RectTransform)root.transform;
                p.staging = stagingGo.transform;
                p.labelTemplate = NativeSettingsWindow.CloneInactive(srcLabel.gameObject, p.staging);
                p.buttonTemplate = NativeSettingsWindow.CloneInactive(srcButton.gameObject, p.staging);
                p.rootRt.anchorMin = p.rootRt.anchorMax = p.rootRt.pivot = new Vector2(0f, 1f);
                p.rootRt.sizeDelta = new Vector2(WidthSmall, 200f);
                p.BuildFrame(srcFrame.gameObject);
                p.BuildBody();
                p.MeasureChrome();
                error = null;
                ModLog.Detail(Lang.T("Painel de status com o visual do jogo criado: ", "Game-styled status panel created: ") + p.Describe(srcFrame));
                return p;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                if (root != null)
                {
                    Destroy(root);
                }
                return null;
            }
        }

        /// <summary>Frame and title plate of the game's Settings window, stretched over the whole panel.</summary>
        private void BuildFrame(GameObject srcFrame)
        {
            // The window's backdrop may sit on the layout above the frame: copy it as the bottom layer.
            Image back = srcFrame.transform.parent != null ? srcFrame.transform.parent.GetComponent<Image>() : null;
            // Only a 9-slice frame sprite: a plain full-screen dimmer behind the game's window must not be copied.
            if (back != null && back.sprite != null && back.type == Image.Type.Sliced && back.sprite.border != Vector4.zero)
            {
                var go = new GameObject("AK_Backdrop", typeof(RectTransform));
                go.transform.SetParent(rootRt, false);
                Image img = go.AddComponent<Image>();
                img.sprite = back.sprite;
                img.type = back.type;
                img.color = back.color;
                img.pixelsPerUnitMultiplier = back.pixelsPerUnitMultiplier;
                img.raycastTarget = true; // clicks on the panel stay on the panel
                var brt = (RectTransform)go.transform;
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;
            }

            GameObject frame = NativeSettingsWindow.CloneInactive(srcFrame, staging);
            // The frame's own buttons (X) hide the panel, like F9.
            foreach (LazyButton b in frame.GetComponentsInChildren<LazyButton>(true))
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(Hide);
            }
            // A layout or size fitter on the frame would fight the size we set.
            foreach (ContentSizeFitter f in frame.GetComponents<ContentSizeFitter>())
            {
                DestroyImmediate(f);
            }
            var rt = (RectTransform)frame.transform;
            frame.transform.SetParent(rootRt, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Text starts a little inside the frame's inner background.
            var mask = frame.transform.Find("BackMask") as RectTransform;
            if (mask != null && mask.anchorMin == Vector2.zero && mask.anchorMax == Vector2.one)
            {
                SidePad = Mathf.Clamp(Mathf.Max(mask.offsetMin.x, -mask.offsetMax.x) + 6f, 10f, 40f);
                bottomPad = Mathf.Clamp(mask.offsetMin.y + 4f, 8f, 40f);
            }

            Transform header = frame.transform.Find("HeaderGroup/Header");
            title = header != null ? header.GetComponent<TextMeshProUGUI>() : null;
            headerRt = frame.transform.Find("HeaderGroup") as RectTransform;
            if (title != null)
            {
                title.text = $"{Plugin.Name} {Plugin.Version}";
            }

        }

        private void BuildBody()
        {
            status = AddSection();
            session = AddSection();
            events = AddSection();
            for (int i = 0; i < 4; i++)
            {
                GameObject go = Instantiate(buttonTemplate, staging, false);
                go.name = "AK_PanelButton" + i;
                go.transform.SetParent(rootRt, false);
                // The game's buttons fit their text (forcing a width did not stick: the drawn part kept the text's
                // size); the layout measures them and lines them up in centered rows.
                buttons.Add(go.GetComponent<UIDialogWindowButton>());
            }
        }

        private Section AddSection()
        {
            var s = new Section
            {
                Header = AddText("AK_SectionHeader"),
                Body = AddText("AK_SectionBody"),
            };
            var line = new GameObject("AK_Line", typeof(RectTransform));
            line.transform.SetParent(rootRt, false);
            Image img = line.AddComponent<Image>();
            img.color = new Color(0.72f, 0.55f, 0.24f, 0.85f); // same golden line as the F11 dividers
            img.raycastTarget = false;
            s.Line = (RectTransform)line.transform;
            // Hidden until it has something to show (a hidden line left at its default 100×100 drew a golden square).
            s.Header.gameObject.SetActive(false);
            s.Line.gameObject.SetActive(false);
            s.Body.gameObject.SetActive(false);
            return s;
        }

        /// <summary>A text with the game's style: a clone of a Settings window label (it keeps the game's per-language font).</summary>
        private TextMeshProUGUI AddText(string name)
        {
            GameObject go = Instantiate(labelTemplate, staging, false);
            go.name = name;
            foreach (Transform child in go.transform.Cast<Transform>().ToList())
            {
                Destroy(child.gameObject);
            }
            foreach (LayoutElement le in go.GetComponents<LayoutElement>())
            {
                Destroy(le);
            }
            foreach (ContentSizeFitter f in go.GetComponents<ContentSizeFitter>())
            {
                Destroy(f);
            }
            go.transform.SetParent(rootRt, false);
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            t.richText = true;
            t.raycastTarget = false;
            t.text = "";
            ForceTextLayout(t);
            texts.Add(t);
            return t;
        }

        /// <summary>
        /// Left/top, wrapping, no margins. Re-applied on every refresh: the game's text style re-applies its own settings
        /// when it refreshes (first print: everything came out right-aligned).
        /// </summary>
        private static void ForceTextLayout(TextMeshProUGUI t)
        {
            if (t.horizontalAlignment != HorizontalAlignmentOptions.Left)
            {
                t.horizontalAlignment = HorizontalAlignmentOptions.Left;
            }
            if (t.verticalAlignment != VerticalAlignmentOptions.Top)
            {
                t.verticalAlignment = VerticalAlignmentOptions.Top;
            }
            if (t.margin != Vector4.zero)
            {
                t.margin = Vector4.zero;
            }
            if (t.enableAutoSizing)
            {
                t.enableAutoSizing = false;
            }
            if (t.textWrappingMode != TextWrappingModes.Normal)
            {
                t.textWrappingMode = TextWrappingModes.Normal;
            }
            if (t.overflowMode != TextOverflowModes.Overflow)
            {
                t.overflowMode = TextOverflowModes.Overflow;
            }
        }

        /// <summary>Space taken by the title plate: the body starts below it.</summary>
        private void MeasureChrome()
        {
            Canvas.ForceUpdateCanvases();
            if (headerRt == null)
            {
                return;
            }
            var corners = new Vector3[4];
            headerRt.GetWorldCorners(corners);
            float bottom = rootRt.InverseTransformPoint(corners[0]).y; // root pivot = top-left: y = 0 at the top edge
            topPad = Mathf.Max(topPad, -bottom + 6f);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (Failed)
            {
                return;
            }
            try
            {
                UpdateUnsafe();
            }
            catch (Exception e)
            {
                Failed = true;
                GuiRect = Rect.zero;
                ModLog.Warn(Lang.T($"Painel com visual do jogo falhou ({e.GetType().Name}: {e.Message}); voltando ao painel simples.",
                    $"Game-styled panel failed ({e.GetType().Name}: {e.Message}); back to the simple panel."));
                ModLog.Detail(e.ToString());
            }
        }

        private void UpdateUnsafe()
        {
            // Same corner rule as the simple panel; anchors follow the option.
            OverlayCorner corner = settings.OverlayPosition.Value;
            if (corner != lastCorner)
            {
                lastCorner = corner;
                ApplyCorner(corner);
            }
            ApplyScale();

            bool pt = GameApi.IsGameLanguagePortuguese();
            bool botOn = bot.State != BotController.BotState.Off;
            if (!buttonsDrawn || pt != lastPt || botOn != lastBotOn)
            {
                buttonsDrawn = true;
                lastPt = pt;
                lastBotOn = botOn;
                DrawButtons(pt, botOn);
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh(pt);
                KeepMaterials();
            }
            if (layoutDirty)
            {
                layoutDirty = false;
                Relayout();
            }
            UpdateGuiRect();
        }

        private void OnDisable() => GuiRect = Rect.zero;

        public void Hide() => settings.ShowOverlay.Value = false;

        private void DrawButtons(bool pt, bool botOn)
        {
            string botText = botOn ? (pt ? "Desligar bot" : "Bot off") : (pt ? "Ligar bot" : "Bot on");
            Draw(0, $"{botText} ({settings.ToggleBotKey.Value})", () => bot.Toggle());
            Draw(1, $"{(pt ? "Configurações" : "Settings")} ({settings.OpenSettingsKey.Value})", () => onSettings?.Invoke());
            Draw(2, $"{(pt ? "Diagnóstico" : "Diagnostic")} ({settings.DumpKey.Value})", () => onDump?.Invoke());
            Draw(3, $"{(pt ? "Fechar" : "Close")} ({settings.ToggleOverlayKey.Value})", Hide);
            layoutDirty = true; // new texts = new button widths
        }

        private void Draw(int i, string text, Action onPressed)
        {
            UIDialogWindowButton b = buttons[i];
            if (b != null)
            {
                // replaceForGamepad=false and key None: the button never takes a gamepad/keyboard key.
                b.Draw(new UIDialogWindowData.ButtonData(onPressed, text, null, false, GameKey.None, text));
            }
        }

        private void Refresh(bool pt)
        {
            foreach (TextMeshProUGUI t in texts)
            {
                ForceTextLayout(t);
            }
            StatusContent c = StatusContent.Build(settings, bot, pt);
            if (title != null)
            {
                SetText(title, c.Title);
            }

            float labelCol = 0f;
            var rows = new List<(string label, string value, bool muted)> { ("Bot:", c.State, false) };
            if (c.Reason != null)
            {
                rows.Add((pt ? "Motivo:" : "Reason:", c.Reason, false));
            }
            rows.AddRange(c.Rows.Where(r => !r.Session).Select(r => (r.Label, r.Value, r.Muted)));
            foreach (var r in rows)
            {
                labelCol = Mathf.Max(labelCol, status.Body.GetPreferredValues(r.label).x);
            }
            int indent = Mathf.CeilToInt(labelCol + 4f);
            var sb = new StringBuilder();
            foreach (var r in rows)
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }
                string value = r.muted ? StatusContent.Color(GameUiTheme.LabelHex, r.value) : StatusContent.Color(GameUiTheme.ValueHex, r.value);
                // One size for everything: the game's pixel font only stays sharp at its own size.
                sb.Append(StatusContent.Color(GameUiTheme.LabelHex, r.label))
                  .Append("<indent=").Append(indent.ToString(CultureInfo.InvariantCulture)).Append('>')
                  .Append(value).Append("</indent>");
            }
            if (c.Note != null)
            {
                sb.Append('\n').Append(c.Note);
            }
            SetSection(status, pt ? "Estado" : "Status", sb.ToString());

            StatusContent.Pair? sess = c.Rows.Where(r => r.Session).Select(r => (StatusContent.Pair?)r).FirstOrDefault();
            SetSection(session, pt ? "Sessão" : "Session", sess.HasValue ? StatusContent.Color(GameUiTheme.ValueHex, sess.Value.Value) : null);

            string ev = null;
            if (c.Events.Count > 0)
            {
                int clockCol = Mathf.CeilToInt(events.Body.GetPreferredValues("00:00").x + 6f);
                ev = string.Join("\n", c.Events.Select(e =>
                    StatusContent.Color(GameUiTheme.MutedHex, e.Clock)
                    + "<indent=" + clockCol.ToString(CultureInfo.InvariantCulture) + ">"
                    + StatusContent.Color(GameUiTheme.TitleHex, e.Text) + "</indent>"));
            }
            SetSection(events, pt ? "Eventos" : "Events", ev);
        }

        private void SetSection(Section s, string header, string body)
        {
            bool visible = !string.IsNullOrEmpty(body);
            if (visible != s.Visible)
            {
                s.Visible = visible;
                s.Header.gameObject.SetActive(visible);
                s.Line.gameObject.SetActive(visible);
                s.Body.gameObject.SetActive(visible);
                layoutDirty = true;
            }
            if (visible)
            {
                SetText(s.Header, StatusContent.Color(GameUiTheme.ValueHex, header));
                SetText(s.Body, body);
            }
        }

        private void SetText(TextMeshProUGUI t, string text)
        {
            if (t.text != text)
            {
                t.text = text;
                layoutDirty = true;
            }
        }

        /// <summary>
        /// The game destroys and recreates its text materials when it rescans mods or changes language; its labels get the
        /// new one, and a text left on a released material draws nothing — fall back to the font's own material.
        /// </summary>
        private void KeepMaterials()
        {
            foreach (TextMeshProUGUI t in texts.Append(title))
            {
                if (t != null && t.fontSharedMaterial == null && t.font != null)
                {
                    t.fontSharedMaterial = t.font.material;
                }
            }
        }

        // ------------------------------------------------------------------ layout

        private void Relayout()
        {
            float inner = panelWidth - SidePad * 2f;
            float y = topPad;
            foreach (Section s in new[] { status, session, events })
            {
                if (!s.Visible)
                {
                    continue;
                }
                y = Place(s.Header, SidePad, y, inner) + 1f;
                Place(s.Line, SidePad, y, inner, 1.5f);
                y += 3f;
                y = Place(s.Body, SidePad, y, inner) + Gap * 2f;
            }
            y = PlaceButtons(y + Gap, inner);
            rootRt.sizeDelta = new Vector2(panelWidth, y + bottomPad);
        }

        /// <summary>The game's buttons at their own width, in rows that fit the panel, each row centered. Returns the bottom.</summary>
        private float PlaceButtons(float y, float inner)
        {
            var row = new List<(RectTransform rt, float width, float inset, float height)>();
            float rowWidth = 0f;
            foreach (UIDialogWindowButton b in buttons)
            {
                if (b == null)
                {
                    continue;
                }
                var rt = (RectTransform)b.transform;
                float w = VisibleBounds(rt, out float inset, out float h);
                if (row.Count > 0 && rowWidth + Gap * 2f + w > inner)
                {
                    y = PlaceRow(row, rowWidth, y, inner);
                    row.Clear();
                    rowWidth = 0f;
                }
                rowWidth += (row.Count > 0 ? Gap * 2f : 0f) + w;
                row.Add((rt, w, inset, h));
            }
            if (row.Count > 0)
            {
                y = PlaceRow(row, rowWidth, y, inner);
            }
            return y;
        }

        private static float PlaceRow(List<(RectTransform rt, float width, float inset, float height)> row, float rowWidth, float y, float inner)
        {
            float x = SidePad + Mathf.Max(0f, (inner - rowWidth) / 2f);
            float h = 0f;
            foreach (var b in row)
            {
                b.rt.anchorMin = b.rt.anchorMax = b.rt.pivot = new Vector2(0f, 1f);
                b.rt.anchoredPosition = new Vector2(x - b.inset, -y);
                x += b.width + Gap * 2f;
                h = Mathf.Max(h, b.height);
            }
            return y + h + Gap;
        }

        /// <summary>
        /// Width of what the button actually draws (its graphics), the offset of that from the button's own left edge,
        /// and its height — after the game's layout has sized it to the text.
        /// </summary>
        private static float VisibleBounds(RectTransform b, out float inset, out float height)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(b);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var corners = new Vector3[4];
            foreach (Graphic g in b.GetComponentsInChildren<Graphic>(false))
            {
                g.rectTransform.GetWorldCorners(corners);
                Vector3 lo = b.InverseTransformPoint(corners[0]);
                Vector3 hi = b.InverseTransformPoint(corners[2]);
                minX = Mathf.Min(minX, lo.x);
                maxX = Mathf.Max(maxX, hi.x);
                minY = Mathf.Min(minY, lo.y);
                maxY = Mathf.Max(maxY, hi.y);
            }
            Rect r = b.rect;
            if (minX > maxX)
            {
                inset = 0f;
                height = r.height;
                return r.width;
            }
            inset = minX - r.xMin;
            height = Mathf.Max(r.height, maxY - minY);
            return maxX - minX;
        }

        /// <summary>Places a text at (x, y) from the panel's top-left corner, with its wrapped height; returns its bottom.</summary>
        private static float Place(TextMeshProUGUI t, float x, float y, float width)
        {
            float h = Mathf.Ceil(t.GetPreferredValues(t.text, width, 0f).y);
            Place(t.rectTransform, x, y, width, h);
            return y + h;
        }

        private static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Whole-panel scale so its pixel art and the game's pixel font land on whole screen pixels: "Small" = 1 screen
        /// pixel per art pixel (2 from 4K up), "Large" = the game's own scale.
        /// </summary>
        private void ApplyScale()
        {
            float ppu = ParentPixelsPerUnit();
            bool large = settings.OverlayScale.Value == OverlaySize.Large;
            float scale = large || ppu <= 0f ? 1f : Mathf.Max(1f, Mathf.Floor(ppu / 2f)) / ppu;
            float width = large ? WidthLarge : WidthSmall;
            if (Mathf.Abs(scale - lastScale) > 0.001f || Math.Abs(width - panelWidth) > 0.01f)
            {
                lastScale = scale;
                panelWidth = width;
                rootRt.localScale = new Vector3(scale, scale, 1f);
                layoutDirty = true;
            }
        }

        /// <summary>Screen pixels per UI unit of the game's UI root (2 at 1080p).</summary>
        private float ParentPixelsPerUnit()
        {
            Transform parent = rootRt.parent;
            if (parent == null)
            {
                return 1f;
            }
            Canvas c = parent.GetComponentInParent<Canvas>();
            Canvas rootCanvas = c != null ? c.rootCanvas : null;
            Camera cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(Vector3.zero));
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(new Vector3(100f, 0f, 0f)));
            return (b - a).magnitude / 100f;
        }

        private void ApplyCorner(OverlayCorner corner)
        {
            Vector2 a;
            Vector2 offset;
            switch (corner)
            {
                case OverlayCorner.TopRight: a = new Vector2(1f, 1f); offset = new Vector2(-Margin, -Margin); break;
                case OverlayCorner.BottomLeft: a = new Vector2(0f, 0f); offset = new Vector2(Margin, Margin); break;
                case OverlayCorner.BottomRight: a = new Vector2(1f, 0f); offset = new Vector2(-Margin, Margin); break;
                default: a = new Vector2(0f, 1f); offset = new Vector2(Margin, -Margin); break;
            }
            // The pivot stays top-left (the layout measures from there); the anchor picks the corner, and the position
            // is corrected by the panel size for the right/bottom corners.
            rootRt.anchorMin = rootRt.anchorMax = a;
            layoutDirty = true;
            cornerOffset = offset;
        }

        private Vector2 cornerOffset = new Vector2(Margin, -Margin);

        private void LateUpdate()
        {
            if (Failed || rootRt == null)
            {
                return;
            }
            // Position from the current size: the size changes with the texts.
            Vector2 size = rootRt.sizeDelta * rootRt.localScale.x; // in the parent's units
            Vector2 a = rootRt.anchorMin;
            var pos = new Vector2(
                cornerOffset.x - (a.x > 0.5f ? size.x : 0f),
                cornerOffset.y + (a.y < 0.5f ? size.y : 0f));
            if (a.y > 0.5f)
            {
                pos.y = Mathf.Min(pos.y, BelowZoneLabel(pos.x, size.x, a.x));
            }
            rootRt.anchoredPosition = pos;
        }

        /// <summary>
        /// Top edge (anchored y, parent units) that keeps a top-corner panel below the game's location name plate when
        /// both would overlap sideways; +infinity when the plate is not on screen or does not get in the way.
        /// </summary>
        private float BelowZoneLabel(float x, float width, float anchorX)
        {
            Rect label = GameApi.GetZoneLabelRectSticky(); // screen pixels, origin top-left; stays below the plate even while it is hidden
            var parent = rootRt.parent as RectTransform;
            if (label.width <= 0f || parent == null)
            {
                return float.PositiveInfinity;
            }
            Canvas c = parent.GetComponentInParent<Canvas>();
            Canvas rootCanvas = c != null ? c.rootCanvas : null;
            Camera cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(label.xMin, Screen.height - label.yMax), cam, out Vector2 bottomLeft)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(label.xMax, Screen.height - label.yMax), cam, out Vector2 bottomRight))
            {
                return float.PositiveInfinity;
            }
            Rect pr = parent.rect;
            float left = Mathf.Lerp(pr.xMin, pr.xMax, anchorX) + x; // the panel's horizontal span in parent units
            if (bottomRight.x <= left || bottomLeft.x >= left + width)
            {
                return float.PositiveInfinity;
            }
            return bottomLeft.y - pr.yMax - Margin;
        }

        private void UpdateGuiRect()
        {
            Canvas c = GetComponentInParent<Canvas>();
            Canvas rootCanvas = c != null ? c.rootCanvas : null;
            Camera cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
            var corners = new Vector3[4];
            rootRt.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            GuiRect = new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
        }

        /// <summary>Structure and sizes (log file), to tune the layout without seeing the screen.</summary>
        private string Describe(Transform srcFrame)
        {
            var sb = new StringBuilder();
            Canvas c = GetComponentInParent<Canvas>();
            sb.Append($"screen={Screen.width}x{Screen.height} scale={(c != null ? c.rootCanvas.scaleFactor : 0f):0.##} ppu={ParentPixelsPerUnit():0.##} ");
            sb.Append($"pads=({SidePad:0.#},{topPad:0.#},{bottomPad:0.#}) ");
            TextMeshProUGUI t = texts.FirstOrDefault();
            if (t != null)
            {
                sb.Append($"text={t.font?.name}/{t.fontSize:0.#} ");
            }
            sb.Append("frame=[");
            foreach (Transform child in srcFrame.Cast<Transform>())
            {
                var rt = (RectTransform)child;
                Image img = child.GetComponent<Image>();
                sb.Append(child.name).Append(' ').Append(rt.rect.size.ToString("0"));
                if (img != null && img.sprite != null)
                {
                    sb.Append(' ').Append(img.sprite.name);
                }
                sb.Append("; ");
            }
            Image bg = srcFrame.GetComponent<Image>();
            sb.Append("] bg=").Append(bg != null && bg.sprite != null ? $"{bg.sprite.name} {bg.type} border={bg.sprite.border}" : "-");
            Image back = srcFrame.parent != null ? srcFrame.parent.GetComponent<Image>() : null;
            sb.Append(" backdrop=").Append(back != null && back.sprite != null ? $"{back.sprite.name} {back.type}" : "-");
            sb.Append(" header=").Append(headerRt != null ? headerRt.rect.size.ToString("0") : "-");
            return sb.ToString();
        }
    }
}
