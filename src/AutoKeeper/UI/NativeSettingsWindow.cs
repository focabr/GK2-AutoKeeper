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
    /// Tela de configurações com o VISUAL NATIVO do jogo.
    ///
    /// Em vez de desenhar botões próprios, clonamos peças da janela de Configurações do próprio jogo
    /// (UIGameSettingsWindow): moldura/cabeçalho, linhas "◀ valor ▶" (UISwitchButton), sliders (UISlider)
    /// e botões de diálogo (UIDialogWindowButton) — mesmas sprites, fontes, cores e sons.
    /// A janela é uma LazyWindow de verdade: entra na pilha de janelas do jogo, que pausa o jogo e bloqueia
    /// o personagem enquanto ela está aberta; Esc fecha.
    ///
    /// Exceção documentada à regra "só a GameApi toca o jogo": esta classe usa componentes de UI do jogo.
    /// Se qualquer peça não for encontrada (jogo atualizado), TryCreate falha e o mod usa a janela simples (IMGUI).
    /// Técnica de captura inspirada no GK2 Mod Framework (SuperMan4eg, MIT).
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

        /// <summary>Esperando o jogador apertar a nova tecla de um atalho.</summary>
        public bool IsCapturingKey => capturing != null || Time.frameCount == captureEndFrame;

        // ------------------------------------------------------------------ criação

        internal static NativeSettingsWindow TryCreate(Settings settings, BotController bot, out string error)
        {
            GameObject root = null;
            try
            {
                UIGameSettingsWindow src = LazyUI.GetWindow<UIGameSettingsWindow>();
                if (src == null)
                {
                    error = "janela de configurações do jogo indisponível";
                    return null;
                }
                Transform srcLayout = src.transform.Find("GenericWIndowLayout");
                var srcSwitch = AccessTools.Field(typeof(UIGameSettingsWindow), "fullscreenButton")?.GetValue(src) as UISwitchButton;
                var srcSlider = AccessTools.Field(typeof(UIGameSettingsWindow), "masterVolumeSlider")?.GetValue(src) as UISlider;
                var srcButton = AccessTools.Field(typeof(UIGameSettingsWindow), "lazyButton")?.GetValue(src) as UIDialogWindowButton;
                if (srcLayout == null || srcSwitch == null || srcSlider == null || srcButton == null
                    || SwitchAmountField == null || SliderField == null || SliderAmountField == null)
                {
                    error = $"peças não encontradas (layout={srcLayout != null}, switch={srcSwitch != null}, slider={srcSlider != null}, button={srcButton != null})";
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

                // Contêiner INATIVO: clones ficam aqui sem rodar Awake/Start até serem configurados.
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
                w.Init(); // LazyWindow: controlador de input, canvas, esconde a janela
                error = null;
                ModLog.Info("Tela de configurações nativa criada (visual da janela de Configurações do jogo).");
                ModLog.Info("UI nativa: " + w.Describe());
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
            // Textos com LocalizedLabel seriam retraduzidos pelo jogo e apagariam os nossos.
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

        /// <summary>Clona a moldura da janela de Configurações do jogo e esvazia a área de conteúdo.</summary>
        private void BuildChrome(GameObject srcLayout)
        {
            GameObject layout = CloneInactive(srcLayout, staging);
            Transform contentT = layout.transform.Find("Content");
            if (contentT == null)
            {
                throw new InvalidOperationException("Content não encontrado na janela do jogo");
            }
            // Remove as linhas do jogo (resolução, volume...) — ficam só moldura, cabeçalho e dicas.
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

            // Título: o TMP do cabeçalho da moldura.
            Transform header = layout.transform.Find("Frame/HeaderGroup/Header");
            titleText = header != null ? header.GetComponent<TextMeshProUGUI>() : null;

            // Botões da moldura (ex.: X) passam a fechar a NOSSA janela.
            foreach (LazyButton b in layout.GetComponentsInChildren<LazyButton>(true))
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(Close);
            }

            layout.transform.SetParent(transform, false);
        }

        /// <summary>Resumo da estrutura clonada (vai para o log para ajustar o layout sem ver a tela).</summary>
        private string Describe()
        {
            string Size(GameObject g) => g == null ? "-" : ((RectTransform)g.transform).rect.size.ToString("0");
            LayoutGroup lg = content != null ? content.GetComponent<LayoutGroup>() : null;
            string children = string.Join(",", transform.Cast<Transform>().SelectMany(t => t.Cast<Transform>()).Select(t => t.name).Take(12));
            return $"content={(content != null ? content.rect.size.ToString("0") : "-")} layout={(lg != null ? lg.GetType().Name : "-")} "
                + $"switch={Size(switchTemplate)} slider={Size(sliderTemplate)} button={Size(buttonTemplate)} "
                + $"title={(titleText != null)} partes=[{children}]";
        }

        // ------------------------------------------------------------------ ciclo de vida (LazyWindow)

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
            // A janela do jogo imprime dicas de controle; sem o componente de dicas não há o que imprimir.
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

        // ------------------------------------------------------------------ conteúdo

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
                titleText.text = $"AutoKeeper {Plugin.Version}";
            }

            // 1) Categoria (◀ Corpos ▶) — igual às opções da janela de Configurações do jogo.
            SettingTab[] tabs = (SettingTab[])Enum.GetValues(typeof(SettingTab));
            // Sem rótulo e centralizada: parece um seletor de "páginas" acima de tudo, não mais uma opção da lista.
            UISwitchButton categorySwitch = AddSwitch("", tabs.Select(TabName).ToArray(), Array.IndexOf(tabs, tab),
                i => { tab = tabs[i]; rebuildPending = true; },
                T("Categoria: escolha o grupo de opções.", "Category: choose the group of options."));

            // Divisor: deixa claro que as opções abaixo pertencem à categoria escolhida acima.
            AddDivider();

            // 2) Opções da categoria.
            foreach (SettingInfo s in settings.UiSettings.Where(x => x.Tab == tab).OrderBy(x => x.Order))
            {
                AddSettingRow(s);
            }

            // 3) Descrição da opção sob o mouse.
            hintText = AddHint();

            // 4) Botões: ligar/desligar bot, restaurar padrões, fechar.
            lastBotOn = bot.State != BotController.BotState.Off;
            botButton = AddButton(BotButtonText(), bot.Toggle, T("Liga ou desliga o bot agora.", "Turns the bot on or off now."));
            AddButton(T("Restaurar padrões desta categoria", "Reset this category"), () =>
            {
                settings.ResetTab(tab);
                rebuildPending = true;
            }, T("Volta as opções desta categoria ao padrão.", "Restores this category's defaults."));
            AddButton(T("Fechar", "Close"), Close, T("Fecha (Esc). As alterações já estão salvas.", "Closes (Esc). Changes are already saved."));

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            CenterSwitch(categorySwitch);
        }

        /// <summary>Esconde o rótulo da linha e centraliza o conjunto ◀ valor ▶ na largura da linha.</summary>
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
                // O valor do jogo (amountLabel) fica dentro de uma moldura maior; usa o pai dela como "campo" se existir.
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
                ModLog.Debug("Não consegui centralizar a categoria: " + e.Message);
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
                Array values = Enum.GetValues(type);
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
            }
            else if (type == typeof(string))
            {
                var e = (ConfigEntry<string>)entry;
                string value = string.IsNullOrEmpty(e.Value) ? T("(automático)", "(automatic)") : e.Value;
                UISwitchButton sw = AddSwitch(label, new[] { value }, 0, null, help + " " + T("(editável no .cfg)", "(editable in the .cfg)"));
                sw.IsInteractable = false;
            }
        }

        // ------------------------------------------------------------------ fábricas de linhas (clones nativos)

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
            // O UISlider do jogo só entende 0..100; trocamos a lógica pela nossa (faixa/passo de cada opção).
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
            // replaceForGamepad=false e tecla None: o botão não "rouba" teclas do controle.
            b.Draw(new UIDialogWindowData.ButtonData(onPressed, text, null, false, GameKey.None, text));
        }

        /// <summary>
        /// Linha dourada fina com um pouco de espaço acima e abaixo. A altura/largura vêm do sizeDelta
        /// (o layout do jogo não usa LayoutElement); 310 de largura = a mesma das linhas "◀ valor ▶".
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
                t.color = style.color;
            }
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            t.text = T("Passe o mouse sobre uma opção para ver o que ela faz.", "Hover an option to see what it does.");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = t.fontSize * 2.4f;
            le.preferredHeight = t.fontSize * 2.4f;
            ((RectTransform)go.transform).sizeDelta = new Vector2(0f, le.preferredHeight);
            built.Add(go);
            return t;
        }

        /// <summary>Move o clone para o conteúdo (ativa) e liga a descrição ao passar o mouse.</summary>
        private void Place(GameObject go, string help)
        {
            go.transform.SetParent(content, false);
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

        /// <summary>Troca o texto do rótulo da linha (o TMP que não é o valor).</summary>
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

        // ------------------------------------------------------------------ captura de tecla

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
                captureEndFrame = Time.frameCount; // a mesma tecla não aciona o atalho neste frame
                rebuildPending = true;
                return;
            }
        }

        private static bool IsModifier(KeyCode k) =>
            k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift
            || k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand
            || k == KeyCode.AltGr;

        // ------------------------------------------------------------------ textos

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
                case SettingTab.Autopsy: return T("Órgãos", "Organs");
                case SettingTab.AutopsyOthers: return T("Outros itens", "Other items");
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
                    case BodyDestination.Grave: return T("Cova (experimental)", "Grave (experimental)");
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
