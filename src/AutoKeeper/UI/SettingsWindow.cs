using System;
using System.Collections.Generic;
using System.Linq;
using AutoKeeper.Bot;
using AutoKeeper.Config;
using AutoKeeper.Core;
using BepInEx.Configuration;
using UnityEngine;

namespace AutoKeeper.UI
{
    /// <summary>
    /// Tela de configurações própria do AutoKeeper (IMGUI). Abre com F11 ou pelo botão do painel.
    /// Desenha as opções a partir de Settings.UiSettings (mesma fonte da ponte do GK2 Mod Framework),
    /// grava direto nas ConfigEntry do BepInEx (o .cfg é salvo automaticamente) e aplica na hora.
    /// Enquanto está aberta: o bot pausa e o jogo não recebe teclas/cliques.
    /// </summary>
    internal sealed class SettingsWindow
    {
        private const int WindowId = 0x4B41; // "KA"

        private readonly Settings settings;
        private readonly BotController bot;

        private Rect rect;
        private bool rectInitialized;
        private Vector2 scroll;
        private SettingTab tab = SettingTab.Bodies;
        private SettingInfo capturing;       // opção de tecla aguardando a nova tecla
        private float scale = 1f;
        private readonly Dictionary<SettingInfo, string> textBuffers = new Dictionary<SettingInfo, string>();

        private GUIStyle windowStyle, titleStyle, labelStyle, helpStyle, valueStyle, buttonStyle, tabStyle, tabActiveStyle, toggleStyle, boxStyle;
        private Texture2D bgTex, rowTex, accentTex, tabTex, buttonTex;
        private int stylesFor = -1;

        public SettingsWindow(Settings settings, BotController bot)
        {
            this.settings = settings;
            this.bot = bot;
        }

        public bool IsOpen { get; private set; }

        /// <summary>Esperando o jogador apertar a nova tecla de um atalho (as hotkeys do mod ficam em pausa).</summary>
        public bool IsCapturingKey => capturing != null;

        /// <summary>Área ocupada pela janela, em coordenadas de GUI (para bloquear cliques no jogo).</summary>
        public Rect Rect => rect;

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                IsOpen = true;
                capturing = null;
                textBuffers.Clear();
            }
        }

        public void Close()
        {
            IsOpen = false;
            capturing = null;
            FlushTextBuffers();
        }

        public void Draw()
        {
            if (!IsOpen)
            {
                return;
            }
            EnsureStyles();
            if (!rectInitialized)
            {
                float w = Mathf.Min(Screen.width - 40f, 760f * scale);
                float h = Mathf.Min(Screen.height - 40f, 620f * scale);
                rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
                rectInitialized = true;
            }

            // Esc fecha (ou cancela a captura de tecla).
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && capturing == null)
            {
                Close();
                e.Use();
                return;
            }

            rect = GUI.Window(WindowId, rect, DrawWindow, GUIContent.none, windowStyle);
            rect.x = Mathf.Clamp(rect.x, 0f, Screen.width - rect.width);
            rect.y = Mathf.Clamp(rect.y, 0f, Screen.height - rect.height);
        }

        // ------------------------------------------------------------------ conteúdo

        private void DrawWindow(int id)
        {
            bool pt = GameApi.IsGameLanguagePortuguese();
            HandleKeyCapture();

            // Cabeçalho (arrastável).
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{Plugin.Name} {Plugin.Version} — {T(pt, "Configurações", "Settings")}", titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", buttonStyle, GUILayout.Width(36f * scale)))
            {
                Close();
            }
            GUILayout.EndHorizontal();

            // Estado do bot + atalho para ligar/desligar.
            GUILayout.BeginHorizontal(boxStyle);
            string state = bot.State == BotController.BotState.Off
                ? T(pt, "<color=#E07A5F>DESLIGADO</color>", "<color=#E07A5F>OFF</color>")
                : T(pt, "<color=#A6D05A>LIGADO</color>", "<color=#A6D05A>ON</color>");
            GUILayout.Label($"{T(pt, "Bot", "Bot")}: {state}   <i>{bot.StateDetail}</i>", labelStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(bot.State == BotController.BotState.Off ? T(pt, "Ligar bot", "Start bot") : T(pt, "Desligar bot", "Stop bot"),
                    buttonStyle, GUILayout.Width(150f * scale)))
            {
                bot.Toggle();
            }
            GUILayout.EndHorizontal();

            // Abas.
            GUILayout.BeginHorizontal();
            foreach (SettingTab t in (SettingTab[])Enum.GetValues(typeof(SettingTab)))
            {
                if (GUILayout.Button(TabName(t, pt), t == tab ? tabActiveStyle : tabStyle))
                {
                    FlushTextBuffers();
                    tab = t;
                    capturing = null;
                    scroll = Vector2.zero;
                }
            }
            GUILayout.EndHorizontal();

            // Linhas da aba.
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (SettingInfo s in settings.UiSettings.Where(x => x.Tab == tab).OrderBy(x => x.Order))
            {
                DrawRow(s, pt);
            }
            GUILayout.EndScrollView();

            // Rodapé.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T(pt, "Restaurar padrões", "Reset defaults"), buttonStyle))
            {
                settings.ResetTab(tab);
                textBuffers.Clear();
                capturing = null;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(T(pt, "Salvo automaticamente · Esc fecha", "Saved automatically · Esc closes"), helpStyle);
            if (GUILayout.Button(T(pt, "Fechar", "Close"), buttonStyle, GUILayout.Width(110f * scale)))
            {
                Close();
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 40f * scale));
        }

        private void DrawRow(SettingInfo s, bool pt)
        {
            GUILayout.BeginVertical(boxStyle);
            ConfigEntryBase entry = s.Entry;
            Type type = entry.SettingType;

            if (type == typeof(bool))
            {
                var e = (ConfigEntry<bool>)entry;
                bool v = GUILayout.Toggle(e.Value, "  " + s.Label(pt), toggleStyle);
                if (v != e.Value)
                {
                    e.Value = v;
                }
            }
            else if (type == typeof(float) || type == typeof(int))
            {
                float current = type == typeof(float) ? ((ConfigEntry<float>)entry).Value : ((ConfigEntry<int>)entry).Value;
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Label(pt), labelStyle, GUILayout.Width(300f * scale));
                float slid = GUILayout.HorizontalSlider(current, s.Min, s.Max, GUILayout.ExpandWidth(true));
                GUILayout.Label(FormatNumber(current, s.Step), valueStyle, GUILayout.Width(70f * scale));
                GUILayout.EndHorizontal();
                float snapped = Mathf.Clamp(Mathf.Round(slid / s.Step) * s.Step, s.Min, s.Max);
                if (Mathf.Abs(snapped - current) > s.Step * 0.25f)
                {
                    if (type == typeof(float))
                    {
                        ((ConfigEntry<float>)entry).Value = (float)Math.Round(snapped, 3);
                    }
                    else
                    {
                        ((ConfigEntry<int>)entry).Value = Mathf.RoundToInt(snapped);
                    }
                }
            }
            else if (type.IsEnum)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Label(pt), labelStyle, GUILayout.Width(300f * scale));
                Array values = Enum.GetValues(type);
                string[] names = values.Cast<object>().Select(v => EnumText(v, pt)).ToArray();
                int index = Array.IndexOf(values, entry.BoxedValue);
                int chosen = GUILayout.SelectionGrid(index, names, names.Length, tabStyle);
                if (chosen != index && chosen >= 0)
                {
                    entry.BoxedValue = values.GetValue(chosen);
                }
                GUILayout.EndHorizontal();
            }
            else if (type == typeof(KeyboardShortcut))
            {
                var e = (ConfigEntry<KeyboardShortcut>)entry;
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Label(pt), labelStyle, GUILayout.Width(300f * scale));
                string text = capturing == s
                    ? T(pt, "Aperte a nova tecla… (Esc cancela)", "Press the new key… (Esc cancels)")
                    : e.Value.ToString();
                if (GUILayout.Button(text, buttonStyle, GUILayout.ExpandWidth(true)))
                {
                    capturing = capturing == s ? null : s;
                }
                if (GUILayout.Button(T(pt, "Nenhuma", "None"), buttonStyle, GUILayout.Width(110f * scale)))
                {
                    e.Value = KeyboardShortcut.Empty;
                    capturing = null;
                }
                GUILayout.EndHorizontal();
            }
            else if (type == typeof(string))
            {
                var e = (ConfigEntry<string>)entry;
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.Label(pt), labelStyle, GUILayout.Width(300f * scale));
                if (!textBuffers.TryGetValue(s, out string buffer))
                {
                    buffer = e.Value ?? string.Empty;
                }
                textBuffers[s] = GUILayout.TextField(buffer, 200, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(T(pt, "Aplicar", "Apply"), buttonStyle, GUILayout.Width(110f * scale)))
                {
                    e.Value = textBuffers[s].Trim();
                    textBuffers.Remove(s);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Label(s.Help(pt), helpStyle);
            GUILayout.EndVertical();
        }

        /// <summary>Captura a próxima tecla (com Ctrl/Shift/Alt) para o atalho selecionado.</summary>
        private void HandleKeyCapture()
        {
            Event e = Event.current;
            if (capturing == null || e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None)
            {
                return;
            }
            if (e.keyCode == KeyCode.Escape)
            {
                capturing = null;
                e.Use();
                return;
            }
            if (IsModifier(e.keyCode))
            {
                return; // espera a tecla principal
            }
            var mods = new List<KeyCode>();
            if (e.control) mods.Add(KeyCode.LeftControl);
            if (e.shift) mods.Add(KeyCode.LeftShift);
            if (e.alt) mods.Add(KeyCode.LeftAlt);
            ((ConfigEntry<KeyboardShortcut>)capturing.Entry).Value = new KeyboardShortcut(e.keyCode, mods.ToArray());
            capturing = null;
            e.Use();
        }

        private void FlushTextBuffers()
        {
            // Texto digitado e não aplicado é descartado ao trocar de aba/fechar (evita salvar valor pela metade).
            textBuffers.Clear();
        }

        // ------------------------------------------------------------------ textos

        private static string T(bool pt, string ptText, string enText) => pt ? ptText : enText;

        private static string TabName(SettingTab t, bool pt)
        {
            switch (t)
            {
                case SettingTab.Bot: return T(pt, "Geral", "General");
                case SettingTab.Bodies: return T(pt, "Corpos", "Bodies");
                case SettingTab.Autopsy: return T(pt, "Extração", "Extraction");
                case SettingTab.Hotkeys: return T(pt, "Teclas", "Hotkeys");
                case SettingTab.Overlay: return T(pt, "Painel", "Panel");
                default: return T(pt, "Avançado", "Advanced");
            }
        }

        private static string EnumText(object v, bool pt)
        {
            if (v is BodyDestination d)
            {
                switch (d)
                {
                    case BodyDestination.Crematorium: return T(pt, "Crematório", "Crematorium");
                    case BodyDestination.LeaveOnTable: return T(pt, "Deixar na mesa", "Leave on table");
                    case BodyDestination.Grave: return T(pt, "Cova (exp.)", "Grave (exp.)");
                }
            }
            if (v is OverlayCorner c)
            {
                switch (c)
                {
                    case OverlayCorner.TopLeft: return T(pt, "Superior esquerdo", "Top left");
                    case OverlayCorner.TopRight: return T(pt, "Superior direito", "Top right");
                    case OverlayCorner.BottomLeft: return T(pt, "Inferior esquerdo", "Bottom left");
                    case OverlayCorner.BottomRight: return T(pt, "Inferior direito", "Bottom right");
                }
            }
            return v.ToString();
        }

        private static string FormatNumber(float v, float step) => step >= 1f ? v.ToString("0") : v.ToString("0.00");

        private static bool IsModifier(KeyCode k) =>
            k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift
            || k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;

        // ------------------------------------------------------------------ estilo

        private void EnsureStyles()
        {
            int key = Screen.height;
            Font gameFont = GameUiTheme.Font;
            if (stylesFor == key && windowStyle != null && (gameFont == null || titleStyle.font == gameFont))
            {
                return;
            }
            stylesFor = key;
            scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 2f);
            int font = Mathf.RoundToInt(16 * scale);

            // Paleta da janela de Configurações do jogo (tela de reserva, usada só se a nativa falhar).
            bgTex = MakeTex(GameUiTheme.PanelInner);
            rowTex = MakeTex(GameUiTheme.Row);
            accentTex = MakeTex(GameUiTheme.ButtonActive);
            tabTex = MakeTex(GameUiTheme.Button);
            buttonTex = MakeTex(GameUiTheme.Button);
            Texture2D hoverTex = MakeTex(GameUiTheme.ButtonHover);

            windowStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(14, 14, 12, 12), border = new RectOffset(0, 0, 0, 0) };
            windowStyle.normal.background = bgTex;
            windowStyle.onNormal.background = bgTex;

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * scale), fontStyle = FontStyle.Bold, richText = true };
            titleStyle.normal.textColor = GameUiTheme.Title;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = font, richText = true, wordWrap = false };
            labelStyle.normal.textColor = GameUiTheme.Label;

            valueStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleRight };
            valueStyle.normal.textColor = GameUiTheme.Value;

            helpStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * scale), wordWrap = true, richText = true };
            helpStyle.normal.textColor = new Color(0.49f, 0.45f, 0.43f);

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = font, padding = new RectOffset(10, 10, 5, 5), border = new RectOffset(0, 0, 0, 0) };
            buttonStyle.normal.background = buttonTex;
            buttonStyle.hover.background = hoverTex;
            buttonStyle.active.background = accentTex;
            buttonStyle.normal.textColor = GameUiTheme.Value;
            buttonStyle.hover.textColor = GameUiTheme.Title;
            buttonStyle.active.textColor = GameUiTheme.Title;

            tabStyle = new GUIStyle(buttonStyle);
            tabStyle.normal.background = tabTex;
            tabStyle.onNormal.background = accentTex;
            tabStyle.onHover.background = accentTex;
            tabStyle.normal.textColor = GameUiTheme.Label;
            tabStyle.onNormal.textColor = GameUiTheme.Title;

            tabActiveStyle = new GUIStyle(tabStyle);
            tabActiveStyle.normal.background = accentTex;
            tabActiveStyle.normal.textColor = GameUiTheme.Title;

            toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = font };
            toggleStyle.normal.textColor = GameUiTheme.Label;
            toggleStyle.onNormal.textColor = GameUiTheme.Value;
            toggleStyle.hover.textColor = GameUiTheme.Title;
            toggleStyle.onHover.textColor = GameUiTheme.Value;

            boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(0, 0, 3, 3) };
            boxStyle.normal.background = rowTex;

            if (gameFont != null)
            {
                foreach (GUIStyle st in new[] { titleStyle, labelStyle, valueStyle, helpStyle, buttonStyle, tabStyle, tabActiveStyle, toggleStyle })
                {
                    st.font = gameFont;
                }
            }
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
