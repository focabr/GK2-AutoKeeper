using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoKeeper.Bot;
using AutoKeeper.Config;
using AutoKeeper.Core;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoKeeper.UI
{
    /// <summary>
    /// Settings window with the game's NATIVE LOOK.
    ///
    /// Instead of drawing our own buttons, we clone parts of the game's own Settings window
    /// (UIGameSettingsWindow): frame/header, "◀ value ▶" rows (UISwitchButton), sliders (UISlider)
    /// and dialog buttons (UIDialogWindowButton) — same sprites, fonts, colors and sounds.
    /// The window is a real LazyWindow: it joins the game's window stack, which pauses the game and locks
    /// the character while it is open; Esc closes it.
    ///
    /// Documented exception to the "only GameApi touches the game" rule: this class uses the game's UI components.
    /// If any part is not found (game updated), TryCreate fails and the mod uses the simple window (IMGUI).
    /// Capture technique inspired by the GK2 Mod Framework (SuperMan4eg, MIT).
    /// </summary>
    internal sealed class NativeSettingsWindow : LazyWindow<LazyWidgetDataBase>
    {
        private static readonly FieldInfo SwitchAmountField = AccessTools.Field(typeof(UISwitchButton), "amountLabel");
        private static readonly FieldInfo SwitchIncField = AccessTools.Field(typeof(UISwitchButton), "increaseButton");
        private static readonly FieldInfo SwitchDecField = AccessTools.Field(typeof(UISwitchButton), "decreaseButton");
        private static readonly FieldInfo SwitchHeaderField = AccessTools.Field(typeof(UISwitchButton), "headerLabel");
        private static readonly FieldInfo SliderField = AccessTools.Field(typeof(UISlider), "slider");
        private static readonly FieldInfo SliderIncField = AccessTools.Field(typeof(UISlider), "increaseButton");
        private static readonly FieldInfo SliderDecField = AccessTools.Field(typeof(UISlider), "decreaseButton");
        private static readonly FieldInfo SliderAmountField = AccessTools.Field(typeof(UISlider), "amountLabel");

        private static KeyCode[] keyboardKeys;

        private Settings settings;
        private BotController bot;
        private Transform staging;
        private GameObject switchTemplate;
        private GameObject sliderTemplate;
        private GameObject buttonTemplate;
        private RectTransform content;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI hintText;
        private UIDialogWindowButton botButton;
        private readonly List<GameObject> built = new List<GameObject>();

        private SettingTab tab = SettingTab.Bodies;
        private SettingInfo capturing;
        private int captureEndFrame = -1;
        private bool rebuildPending;
        private bool lastBotOn;
        private bool pt;

        /// <summary>Waiting for the player to press the new key for a shortcut.</summary>
        public bool IsCapturingKey => capturing != null || Time.frameCount == captureEndFrame;

        // ------------------------------------------------------------------ creation

        internal static NativeSettingsWindow TryCreate(Settings settings, BotController bot, out string error)
        {
            GameObject root = null;
            try
            {
                UIGameSettingsWindow src = LazyUI.GetWindow<UIGameSettingsWindow>();
                if (src == null)
                {
                    error = Lang.T("janela de configurações do jogo indisponível", "game settings window unavailable");
                    return null;
                }
                Transform srcLayout = src.transform.Find("GenericWIndowLayout");
                var srcSwitch = AccessTools.Field(typeof(UIGameSettingsWindow), "fullscreenButton")?.GetValue(src) as UISwitchButton;
                var srcSlider = AccessTools.Field(typeof(UIGameSettingsWindow), "masterVolumeSlider")?.GetValue(src) as UISlider;
                var srcButton = AccessTools.Field(typeof(UIGameSettingsWindow), "lazyButton")?.GetValue(src) as UIDialogWindowButton;
                if (srcLayout == null || srcSwitch == null || srcSlider == null || srcButton == null
                    || SwitchAmountField == null || SliderField == null || SliderAmountField == null)
                {
                    error = Lang.T($"peças não encontradas (layout={srcLayout != null}, switch={srcSwitch != null}, slider={srcSlider != null}, button={srcButton != null})",
                        $"parts not found (layout={srcLayout != null}, switch={srcSwitch != null}, slider={srcSlider != null}, button={srcButton != null})");
                    return null;
                }

                root = new GameObject("AutoKeeperSettingsWindow", typeof(RectTransform));
                root.transform.SetParent(FindUiRoot(src), false);
                CopyRect((RectTransform)src.transform, (RectTransform)root.transform);

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 460;
                root.AddComponent<GraphicRaycaster>();
                GamepadNavigationController nav = root.AddComponent<GamepadNavigationController>();
                nav.navigationGroupSources = new List<GamepadNavigationController.NavigationGroupSource>();
                nav.useGridSkippedIfListEmpty = true;

                // INACTIVE container: clones stay here without running Awake/Start until they are configured.
                var stagingGo = new GameObject("AK_Templates", typeof(RectTransform));
                stagingGo.transform.SetParent(root.transform, false);
                stagingGo.SetActive(false);

                NativeSettingsWindow w = root.AddComponent<NativeSettingsWindow>();
                w.settings = settings;
                w.bot = bot;
                w.staging = stagingGo.transform;
                w.switchTemplate = CloneInactive(srcSwitch.gameObject, w.staging);
                w.sliderTemplate = CloneInactive(srcSlider.gameObject, w.staging);
                w.buttonTemplate = CloneInactive(srcButton.gameObject, w.staging);
                w.BuildChrome(srcLayout.gameObject);
                w.Init(); // LazyWindow: input controller, canvas, hides the window
                error = null;
                ModLog.Detail(Lang.T("Tela de configurações nativa criada (visual da janela de Configurações do jogo).",
                    "Native settings window created (look of the game's Settings window)."));
                ModLog.Detail(Lang.T("UI nativa: ", "Native UI: ") + w.Describe());
                return w;
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

        private static Transform FindUiRoot(Component sourceWindow)
        {
            GUIElements gui = GUIElements.Instance;
            var fitter = gui == null ? null : AccessTools.Field(typeof(GUIElements), "uiFitter")?.GetValue(gui) as Component;
            return fitter != null ? fitter.transform : sourceWindow.transform.parent;
        }

        private static GameObject CloneInactive(GameObject template, Transform inactiveParent)
        {
            GameObject clone = Instantiate(template, inactiveParent, false);
            clone.name = template.name + "_AK";
            // Texts with a LocalizedLabel would be re-translated by the game and overwrite ours.
            foreach (LocalizedLabel l in clone.GetComponentsInChildren<LocalizedLabel>(true))
            {
                DestroyImmediate(l);
            }
            return clone;
        }

        private static void CopyRect(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.anchoredPosition = from.anchoredPosition;
            to.sizeDelta = from.sizeDelta;
            to.localScale = Vector3.one;
        }

        /// <summary>Clones the frame of the game's Settings window and empties the content area.</summary>
        private void BuildChrome(GameObject srcLayout)
        {
            GameObject layout = CloneInactive(srcLayout, staging);
            Transform contentT = layout.transform.Find("Content");
            if (contentT == null)
            {
                throw new InvalidOperationException(Lang.T("Content não encontrado na janela do jogo", "Content not found in the game's window"));
            }
            // Removes the game's rows (resolution, volume...) — only the frame, header and tips remain.
            for (int i = contentT.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(contentT.GetChild(i).gameObject);
            }
            content = (RectTransform)contentT;
            if (content.GetComponent<LayoutGroup>() == null)
            {
                VerticalLayoutGroup v = content.gameObject.AddComponent<VerticalLayoutGroup>();
                v.spacing = 4f;
                v.childControlWidth = true;
                v.childForceExpandWidth = true;
                v.childControlHeight = false;
                v.childForceExpandHeight = false;
            }

            // Title: the TMP of the frame's header.
            Transform header = layout.transform.Find("Frame/HeaderGroup/Header");
            titleText = header != null ? header.GetComponent<TextMeshProUGUI>() : null;

            // Frame buttons (e.g. X) now close OUR window.
            foreach (LazyButton b in layout.GetComponentsInChildren<LazyButton>(true))
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(Close);
            }

            layout.transform.SetParent(transform, false);
        }

        /// <summary>Summary of the cloned structure (goes to the log, to tune the layout without seeing the screen).</summary>
        private string Describe()
        {
            string Size(GameObject g) => g == null ? "-" : ((RectTransform)g.transform).rect.size.ToString("0");
            LayoutGroup lg = content != null ? content.GetComponent<LayoutGroup>() : null;
            string children = string.Join(",", transform.Cast<Transform>().SelectMany(t => t.Cast<Transform>()).Select(t => t.name).Take(12));
            return $"content={(content != null ? content.rect.size.ToString("0") : "-")} layout={(lg != null ? lg.GetType().Name : "-")} "
                + $"switch={Size(switchTemplate)} slider={Size(sliderTemplate)} button={Size(buttonTemplate)} "
                + $"title={(titleText != null)} {Lang.T("partes", "parts")}=[{children}]";
        }

        // ------------------------------------------------------------------ lifecycle (LazyWindow)

        public override void Redraw()
        {
            base.Redraw();
            BuildContent();
        }

        protected override void TestDraw()
        {
        }

        protected override bool OnPressedBack()
        {
            if (capturing != null)
            {
                capturing = null;
                captureEndFrame = Time.frameCount;
                rebuildPending = true;
                return true;
            }
            Close();
            return true;
        }

        protected override void PrintTips()
        {
            // The game's window prints control tips; without the tips component there is nothing to print.
            if (lazyButtonTips != null)
            {
                base.PrintTips();
            }
        }

        public override void Close()
        {
            capturing = null;
            base.Close();
        }

        protected override void Update()
        {
            base.Update();
            HandleKeyCapture();

            bool on = bot.State != BotController.BotState.Off;
            if (on != lastBotOn && botButton != null)
            {
                lastBotOn = on;
                DrawButton(botButton, BotButtonText(), bot.Toggle);
            }
            if (rebuildPending)
            {
                rebuildPending = false;
                BuildContent();
            }
        }

        // ------------------------------------------------------------------ content

        private void BuildContent()
        {
            pt = GameApi.IsGameLanguagePortuguese();
            foreach (GameObject g in built)
            {
                if (g != null)
                {
                    Destroy(g);
                }
            }
            built.Clear();

            if (titleText != null)
            {
                titleText.text = $"{Plugin.Name} {Plugin.Version}";
            }

            // 1) Category (◀ Bodies ▶) — like the options in the game's Settings window.
            SettingTab[] tabs = (SettingTab[])Enum.GetValues(typeof(SettingTab));
            // No label and centered: it looks like a "page" selector above everything, not just another option in the list.
            UISwitchButton categorySwitch = AddSwitch("", tabs.Select(TabName).ToArray(), Array.IndexOf(tabs, tab),
                i => { tab = tabs[i]; rebuildPending = true; },
                T("Categoria: escolha o grupo de opções.", "Category: choose the group of options."));

            // Divider: makes it clear that the options below belong to the category chosen above.
            AddDivider();

            // 2) The category's options.
            foreach (SettingInfo s in settings.UiSettings.Where(x => x.Tab == tab).OrderBy(x => x.Order))
            {
                AddSettingRow(s);
            }

            // 3) Description of the option under the mouse.
            hintText = AddHint();

            // 4) Buttons: start/stop bot, reset defaults, close.
            // 4) Window actions: start/stop highlighted; reset and close side by side.
            AddDivider();
            lastBotOn = bot.State != BotController.BotState.Off;
            botButton = AddButton(BotButtonText(), bot.Toggle,
                T("Liga ou desliga o bot agora (o mesmo que a tecla F8).", "Turns the bot on or off now (same as the F8 key)."));
            FixWidth(botButton, 308f); // same width as the two below combined
            AddButtonRow(
                (T("Restaurar padrões", "Reset defaults"), () =>
                {
                    settings.ResetTab(tab);
                    rebuildPending = true;
                }, T("Volta só as opções desta categoria ao valor original.", "Resets only this category's options to their original values.")),
                (T("Fechar", "Close"), Close,
                    T("Fecha a janela (Esc). As alterações já ficam salvas.", "Closes the window (Esc). Changes are already saved.")));

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            CenterSwitch(categorySwitch);
        }

        /// <summary>Hides the row's label and centers the ◀ value ▶ group across the row's width.</summary>
        private static void CenterSwitch(UISwitchButton sw)
        {
            try
            {
                var inc = (SwitchIncField?.GetValue(sw) as Component)?.transform as RectTransform;
                var dec = (SwitchDecField?.GetValue(sw) as Component)?.transform as RectTransform;
                var amount = (SwitchAmountField?.GetValue(sw) as TextMeshProUGUI)?.rectTransform;
                var header = SwitchHeaderField?.GetValue(sw) as TextMeshProUGUI;
                var row = sw.transform as RectTransform;
                if (inc == null || dec == null || amount == null || row == null)
                {
                    return;
                }
                if (header != null)
                {
                    header.text = "";
                }
                // The game's value (amountLabel) sits inside a larger frame; use its parent as the "field" if there is one.
                RectTransform field = amount.parent != null && amount.parent != row && amount.parent is RectTransform pr && pr.parent == inc.parent
                    ? pr
                    : amount;
                RectTransform[] parts = { inc, dec, field };
                var corners = new Vector3[4];
                bool first = true;
                var bounds = new Bounds();
                foreach (RectTransform rt in parts)
                {
                    rt.GetWorldCorners(corners);
                    foreach (Vector3 c in corners)
                    {
                        Vector3 local = row.InverseTransformPoint(c);
                        if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                        else { bounds.Encapsulate(local); }
                    }
                }
                float shift = row.rect.center.x - bounds.center.x;
                if (Mathf.Abs(shift) < 0.5f)
                {
                    return;
                }
                foreach (RectTransform rt in parts)
                {
                    rt.anchoredPosition += new Vector2(shift, 0f);
                }
            }
            catch (Exception e)
            {
                ModLog.Debug(Lang.T("Não consegui centralizar a categoria: " + e.Message, "Could not center the category: " + e.Message));
            }
        }

        private void AddSettingRow(SettingInfo s)
        {
            ConfigEntryBase entry = s.Entry;
            Type type = entry.SettingType;
            string label = s.Label(pt);
            string help = s.Help(pt);

            if (type == typeof(bool))
            {
                var e = (ConfigEntry<bool>)entry;
                AddSwitch(label, new[] { T("Não", "No"), T("Sim", "Yes") }, e.Value ? 1 : 0, i => e.Value = i == 1, help);
            }
            else if (type.IsEnum)
            {
                Array values = Settings.VisibleValues(type);
                string[] names = values.Cast<object>().Select(EnumText).ToArray();
                AddSwitch(label, names, Array.IndexOf(values, entry.BoxedValue), i => entry.BoxedValue = values.GetValue(i), help);
            }
            else if (type == typeof(float))
            {
                var e = (ConfigEntry<float>)entry;
                AddSlider(label, s, e.Value, v => e.Value = (float)Math.Round(v, 3), help);
            }
            else if (type == typeof(int))
            {
                var e = (ConfigEntry<int>)entry;
                AddSlider(label, s, e.Value, v => e.Value = Mathf.RoundToInt(v), help);
            }
            else if (type == typeof(KeyboardShortcut))
            {
                var e = (ConfigEntry<KeyboardShortcut>)entry;
                UIDialogWindowButton b = null;
                b = AddButton(KeyText(s, e), () =>
                {
                    capturing = s;
                    DrawButton(b, $"{label}: {T("aperte a nova tecla… (Esc cancela)", "press the new key… (Esc cancels)")}", null);
                }, help + " " + T("Clique e aperte a nova tecla.", "Click, then press the new key."));
                FixWidth(b, 270f); // all the same size, aligned
            }
            else if (type == typeof(string))
            {
                var e = (ConfigEntry<string>)entry;
                string value = string.IsNullOrEmpty(e.Value) ? T("(automático)", "(automatic)") : e.Value;
                UISwitchButton sw = AddSwitch(label, new[] { value }, 0, null, help + " " + T("(editável no .cfg)", "(editable in the .cfg)"));
                sw.IsInteractable = false;
            }
        }

        // ------------------------------------------------------------------ row factories (native clones)

        private UISwitchButton AddSwitch(string label, string[] fields, int index, Action<int> onChanged, string help)
        {
            GameObject go = Instantiate(switchTemplate, staging, false);
            UISwitchButton sw = go.GetComponent<UISwitchButton>();
            sw.Initialize(i => onChanged?.Invoke(i), fields, Mathf.Max(0, index), label);
            SetHeader(go, SwitchHeaderField?.GetValue(sw) as TextMeshProUGUI, SwitchAmountField.GetValue(sw) as TextMeshProUGUI, label);
            Place(go, help);
            return sw;
        }

        private void AddSlider(string label, SettingInfo s, float current, Action<float> onChanged, string help)
        {
            GameObject go = Instantiate(sliderTemplate, staging, false);
            UISlider ui = go.GetComponent<UISlider>();
            var slider = (Slider)SliderField.GetValue(ui);
            var inc = SliderIncField?.GetValue(ui) as Button;
            var dec = SliderDecField?.GetValue(ui) as Button;
            var amount = (TextMeshProUGUI)SliderAmountField.GetValue(ui);
            // The game's UISlider only understands 0..100; we replace its logic with ours (each option's range/step).
            DestroyImmediate(ui);

            float step = s.Step > 0f ? s.Step : 1f;
            int steps = Mathf.Max(1, Mathf.RoundToInt((s.Max - s.Min) / step));
            slider.onValueChanged.RemoveAllListeners();
            slider.minValue = 0f;
            slider.maxValue = steps;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(Mathf.Clamp(Mathf.Round((current - s.Min) / step), 0, steps));
            amount.text = FormatNumber(current, step);
            slider.onValueChanged.AddListener(x =>
            {
                float v = Mathf.Clamp(s.Min + x * step, s.Min, s.Max);
                amount.text = FormatNumber(v, step);
                onChanged(v);
            });
            inc?.onClick.AddListener(() => slider.value = Mathf.Min(slider.maxValue, slider.value + 1));
            dec?.onClick.AddListener(() => slider.value = Mathf.Max(slider.minValue, slider.value - 1));

            SetHeader(go, null, amount, label);
            Place(go, help);
        }

        /// <summary>
        /// Several buttons side by side, close together and centered (the game's own buttons). No LayoutGroup: each button
        /// is placed by hand around the row's center, so they don't spread across the window's width.
        /// </summary>
        private void AddButtonRow(params (string text, Action onPressed, string help)[] items)
        {
            const float width = 150f;
            const float gap = 8f;
            var row = new GameObject("AK_ButtonRow", typeof(RectTransform));
            row.transform.SetParent(content, false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(2f * width + gap, 26f);
            LayoutElement rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 26f;
            rowLe.preferredHeight = 26f;
            built.Add(row);
            for (int i = 0; i < items.Length; i++)
            {
                (string text, Action onPressed, string help) = items[i];
                GameObject go = Instantiate(buttonTemplate, staging, false);
                UIDialogWindowButton b = go.GetComponent<UIDialogWindowButton>();
                Place(go, help, row.transform);
                var fit = go.GetComponent<ContentSizeFitter>();
                if (fit != null)
                {
                    fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                }
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(width, 26f);
                rt.anchoredPosition = new Vector2((i - (items.Length - 1) / 2f) * (width + gap), 0f);
                DrawButton(b, text, onPressed);
            }
        }

        /// <summary>Fixed width for a game button (by default it fits its text).</summary>
        private static void FixWidth(UIDialogWindowButton b, float width)
        {
            if (b == null)
            {
                return;
            }
            var fit = b.GetComponent<ContentSizeFitter>();
            if (fit != null)
            {
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            LayoutElement le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
        }

        private UIDialogWindowButton AddButton(string text, Action onPressed, string help)
        {
            GameObject go = Instantiate(buttonTemplate, staging, false);
            UIDialogWindowButton b = go.GetComponent<UIDialogWindowButton>();
            Place(go, help);
            DrawButton(b, text, onPressed);
            return b;
        }

        private static void DrawButton(UIDialogWindowButton b, string text, Action onPressed)
        {
            if (b == null)
            {
                return;
            }
            // replaceForGamepad=false and key None: the button doesn't "steal" gamepad keys.
            b.Draw(new UIDialogWindowData.ButtonData(onPressed, text, null, false, GameKey.None, text));
        }

        /// <summary>
        /// Thin golden line with a little space above and below. Height/width come from sizeDelta
        /// (the game's layout doesn't use LayoutElement); 310 wide = the same as the "◀ value ▶" rows.
        /// </summary>
        private void AddDivider()
        {
            var go = new GameObject("AK_Divider", typeof(RectTransform));
            go.transform.SetParent(content, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(310f, 14f);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 14f;
            le.preferredHeight = 14f;
            le.minWidth = 310f;
            le.preferredWidth = 310f;

            var line = new GameObject("Line", typeof(RectTransform));
            line.transform.SetParent(go.transform, false);
            Image img = line.AddComponent<Image>();
            img.color = new Color(0.72f, 0.55f, 0.24f, 0.85f);
            img.raycastTarget = false;
            var rt = (RectTransform)line.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 1.5f);
            built.Add(go);
        }

        private TextMeshProUGUI AddHint()
        {
            TextMeshProUGUI style = switchTemplate.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            var go = new GameObject("AK_Hint", typeof(RectTransform));
            go.transform.SetParent(content, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            if (style != null)
            {
                t.font = style.font;
                t.fontSharedMaterial = style.fontSharedMaterial;
                t.fontSize = Mathf.Max(12f, style.fontSize * 0.8f);
                t.color = new Color(0.80f, 0.74f, 0.62f, 1f); // light, opaque text, readable over the window background
            }
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            // Long descriptions shrink to fit the box (never spill over the window); what still does not fit ends in "…".
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Max(8f, t.fontSize * 0.65f);
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.text = T("Passe o mouse sobre uma opção para ver o que ela faz.", "Hover an option to see what it does.");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = t.fontSize * 4.4f;
            le.preferredHeight = t.fontSize * 4.4f;
            // The game's layout ignores LayoutElement: the width must be explicit (same as the divider), otherwise the
            // box is 0 wide and the text wraps one letter per line down the middle of the screen.
            ((RectTransform)go.transform).sizeDelta = new Vector2(310f, le.preferredHeight);
            built.Add(go);
            return t;
        }

        /// <summary>Moves the clone into the content (activating it) and shows its description on mouse hover.</summary>
        private void Place(GameObject go, string help, Transform parent = null)
        {
            go.transform.SetParent(parent != null ? parent : content, false);
            if (!string.IsNullOrEmpty(help))
            {
                EventTrigger trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ =>
                {
                    if (hintText != null)
                    {
                        hintText.text = help;
                    }
                });
                trigger.triggers.Add(enter);
            }
            built.Add(go);
        }

        /// <summary>Replaces the text of the row's label (the TMP that is not the value).</summary>
        private static void SetHeader(GameObject row, TextMeshProUGUI header, TextMeshProUGUI amount, string label)
        {
            if (header == null)
            {
                header = row.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t != amount);
            }
            if (header != null)
            {
                header.text = label;
            }
        }

        // ------------------------------------------------------------------ key capture

        private void HandleKeyCapture()
        {
            if (capturing == null || !Input.anyKeyDown)
            {
                return;
            }
            if (keyboardKeys == null)
            {
                keyboardKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
                    .Where(k => k != KeyCode.None && k < KeyCode.Mouse0 && !IsModifier(k))
                    .Distinct()
                    .ToArray();
            }
            foreach (KeyCode k in keyboardKeys)
            {
                if (!Input.GetKeyDown(k))
                {
                    continue;
                }
                if (k != KeyCode.Escape)
                {
                    var mods = new List<KeyCode>();
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods.Add(KeyCode.LeftControl);
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mods.Add(KeyCode.LeftShift);
                    if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mods.Add(KeyCode.LeftAlt);
                    ((ConfigEntry<KeyboardShortcut>)capturing.Entry).Value = new KeyboardShortcut(k, mods.ToArray());
                }
                capturing = null;
                captureEndFrame = Time.frameCount; // the same key doesn't trigger the shortcut in this frame
                rebuildPending = true;
                return;
            }
        }

        private static bool IsModifier(KeyCode k) =>
            k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift
            || k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand
            || k == KeyCode.AltGr;

        // ------------------------------------------------------------------ texts

        private string T(string ptText, string enText) => pt ? ptText : enText;

        private string BotButtonText() => bot.State == BotController.BotState.Off
            ? T("Ligar bot", "Start bot")
            : T("Desligar bot", "Stop bot");

        private string KeyText(SettingInfo s, ConfigEntry<KeyboardShortcut> e) =>
            $"{s.Label(pt)}: {(e.Value.MainKey == KeyCode.None ? T("nenhuma", "none") : e.Value.ToString())}";

        private string TabName(SettingTab t)
        {
            switch (t)
            {
                case SettingTab.Bot: return T("Geral", "General");
                case SettingTab.Bodies: return T("Corpos", "Bodies");
                case SettingTab.Autopsy: return T("Extração", "Extraction");
                case SettingTab.Hotkeys: return T("Teclas", "Hotkeys");
                case SettingTab.Overlay: return T("Painel", "Panel");
                default: return T("Avançado", "Advanced");
            }
        }

        private string EnumText(object v)
        {
            if (v is BodyDestination d)
            {
                switch (d)
                {
                    case BodyDestination.Crematorium: return T("Crematório", "Crematorium");
                    case BodyDestination.LeaveOnTable: return T("Deixar na mesa", "Leave on table");
                    case BodyDestination.Grave: return T("Túmulo (experimental)", "Grave (experimental)");
                }
            }
            if (v is LackOfSleepAction ls)
            {
                switch (ls)
                {
                    case LackOfSleepAction.Stop: return T("Desligar o bot", "Turn off the bot");
                    case LackOfSleepAction.Sleep: return T("Dormir e continuar", "Sleep, then resume");
                    case LackOfSleepAction.KeepWorking: return T("Continuar trabalhando", "Keep working");
                }
            }
            if (v is OverlayCorner c)
            {
                switch (c)
                {
                    case OverlayCorner.TopLeft: return T("Superior esquerdo", "Top left");
                    case OverlayCorner.TopRight: return T("Superior direito", "Top right");
                    case OverlayCorner.BottomLeft: return T("Inferior esquerdo", "Bottom left");
                    case OverlayCorner.BottomRight: return T("Inferior direito", "Bottom right");
                }
            }
            return v.ToString();
        }

        private static string FormatNumber(float v, float step) => step >= 1f ? v.ToString("0") : v.ToString("0.00");
    }
}
