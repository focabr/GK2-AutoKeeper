using System;
using System.Collections.Generic;
using System.Linq;
using AutoKeeper.Config;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.Bot
{
    /// <summary>
    /// Máquina de estados + fila de tarefas. Roda em "ticks" (intervalo configurável), nunca a cada frame.
    /// Pausa sozinho em menu/pausa/janela/diálogo/cinemática e desliga com energia baixa ou erro.
    /// </summary>
    internal sealed class BotController
    {
        public enum BotState
        {
            Off,
            Paused,
            Idle,
            Running,
        }

        private readonly Settings settings;
        private readonly List<ITask> tasks = new List<ITask>();
        private ITask current;
        private float accumulator;

        // Comer da barra rápida (energia baixa).
        private const float EatTimeout = 3f;
        private const float EatKeyDelay = 0.6f;     // espera o personagem sair do estado de trabalho antes de apertar a tecla
        private const int EatKeyAttempts = 2;
        private bool eatKeySent;
        private float eatKeySentAt;
        private int eatKeyTries;
        private const int MaxEatsInARow = 12;
        private bool eating;
        private float eatStartedAt;
        private float energyBeforeEat;
        private int countBeforeEat;
        private HotBarFood eatingFood;
        private int eatsInARow;
        private bool eatBroken;             // a tecla não teve efeito: não tenta de novo até religar o bot
        private float eatRetryAt;           // carregando algo, a tecla pode não funcionar: tenta de novo depois
        private bool toppingUp;             // já comeu nesta parada: continua enquanto a comida couber inteira na energia
        private readonly HashSet<string> skippedFoodLogged = new HashSet<string>();
        private bool memoryClean;           // memória já zerada e o bot não rodou desde então (evita avisos repetidos)

        public BotState State { get; private set; } = BotState.Off;

        /// <summary>Motivo do estado atual (pausa, ociosidade, desligamento).</summary>
        public string StateDetail { get; private set; } = "desligado";

        /// <summary>Última leitura do jogo (atualizada a cada tick, mesmo com o bot desligado, para o overlay).</summary>
        public GameSnapshot LastSnapshot { get; private set; } = new GameSnapshot();

        public string CurrentTaskText => eating ? $"comendo {eatingFood.ItemId}" : current == null ? "-" : $"{current.Name}: {current.Status}";

        /// <summary>Rotas entre áreas (portas), compartilhadas pelas tarefas.</summary>
        public Navigator Navigator { get; } = new Navigator();

        public BotController(Settings settings)
        {
            this.settings = settings;
        }

        /// <summary>Adiciona uma rotina à fila (ordem = prioridade).</summary>
        public void Register(ITask task)
        {
            tasks.Add(task);
        }

        public void Toggle()
        {
            if (State == BotState.Off)
            {
                Start();
            }
            else
            {
                Stop("desligado pelo jogador (hotkey)");
            }
        }

        public void Start()
        {
            eating = false;
            eatBroken = false;
            eatRetryAt = 0f;
            eatsInARow = 0;
            toppingUp = false;
            Navigator.Reset();
            memoryClean = false;
            State = BotState.Idle;
            StateDetail = "aguardando";
            ModLog.Info("Bot LIGADO.");
            if (tasks.Count == 0)
            {
                ModLog.Info("Nenhuma tarefa registrada ainda — o bot só monitora o estado.");
            }
        }

        public void Stop(string reason)
        {
            if (State == BotState.Off)
            {
                return;
            }
            AbortCurrent();
            eating = false;
            toppingUp = false;
            State = BotState.Off;
            StateDetail = reason;
            ModLog.Info($"Bot DESLIGADO: {reason}");
        }

        public void Update(float unscaledDeltaTime)
        {
            accumulator += unscaledDeltaTime;
            if (accumulator < settings.TickIntervalSeconds.Value)
            {
                return;
            }
            accumulator = 0f;

            if (GameApi.ConsumeWorldChange(out string change))
            {
                ResetForNewWorld(change);
            }

            LastSnapshot = StateReader.Read();
            if (State != BotState.Off)
            {
                Tick(LastSnapshot);
            }
        }

        /// <summary>Novo load do save (ou volta ao menu): desliga o bot e zera toda a memória interna.</summary>
        private void ResetForNewWorld(string why)
        {
            Stop($"{why} — memória do bot zerada");
            foreach (ITask t in tasks)
            {
                try { t.ResetMemory(); } catch (System.Exception e) { ModLog.Warn($"Falha ao zerar {t.Name}: {e.Message}"); }
            }
            current = null;
            Navigator.Reset();
            skippedFoodLogged.Clear();
            eatBroken = false;
            eatsInARow = 0;
            toppingUp = false;
            // Menu → Continuar dispara até 3 eventos seguidos (menu, troca do PlayerData, partida carregada): zera em
            // todos, mas só avisa uma vez enquanto o bot não tiver rodado de novo.
            string msg = $"Memória do bot zerada ({why}).";
            if (memoryClean)
            {
                ModLog.Debug(msg + " (já estava zerada)");
                return;
            }
            memoryClean = true;
            ModLog.Info(msg + " Ligue de novo com a tecla do bot quando quiser.");
        }

        private void Tick(GameSnapshot s)
        {
            // 1) Pausa automática: nunca agir com menu, janela, diálogo ou cinemática.
            if (s.BlockReason != null)
            {
                if (State != BotState.Paused)
                {
                    AbortCurrent(); // solta teclas/movimento; a tarefa replaneja ao voltar
                    eating = false;
                    ModLog.Debug($"Bot pausado: {s.BlockReason}");
                }
                State = BotState.Paused;
                StateDetail = s.BlockReason;
                return;
            }

            // 2) Energia baixa: come da barra rápida (se ativado); sem comida, desliga com segurança.
            if (eating)
            {
                TickEating(s);
                return;
            }
            bool energyKnown = s.EnergyMax > 0f && s.Energy >= 0f;
            // Uma parada só para comer: abaixo do limite come e segue comendo enquanto a comida couber inteira na energia
            // que falta (ex.: 19 → 49 → 79 de 86), em vez de interromper o trabalho a cada extração.
            if (energyKnown && (s.Energy < settings.EatBelowEnergy.Value || toppingUp) && TryStartEating(s))
            {
                return;
            }
            toppingUp = false;
            if (energyKnown && s.Energy >= settings.EatBelowEnergy.Value)
            {
                eatsInARow = 0;
            }
            if (energyKnown && s.Energy < settings.MinEnergy.Value)
            {
                Stop($"energia baixa ({s.Energy:0} < {settings.MinEnergy.Value:0})");
                return;
            }

            // 3) Escolhe a primeira tarefa que pode rodar.
            if (current == null)
            {
                string lastReason = tasks.Count == 0 ? "nenhuma tarefa implementada ainda" : "nada a fazer";
                foreach (ITask t in tasks)
                {
                    if (SafeCanRun(t, out string reason))
                    {
                        current = t;
                        ModLog.Info($"Iniciando tarefa: {t.Name}");
                        break;
                    }
                    lastReason = $"{t.Name}: {reason}";
                }
                if (current == null)
                {
                    State = BotState.Idle;
                    StateDetail = lastReason;
                    return;
                }
            }

            // 4) Avança a tarefa um passo.
            State = BotState.Running;
            StateDetail = current.Name;
            TaskResult result;
            try
            {
                result = current.Tick();
            }
            catch (Exception e)
            {
                ModLog.Error($"Erro na tarefa {current.Name}: {e}");
                Stop($"erro na tarefa {current.Name}");
                return;
            }

            switch (result)
            {
                case TaskResult.Succeeded:
                    ModLog.Info($"Tarefa concluída: {current.Name}");
                    current = null;
                    break;
                case TaskResult.Failed:
                    string why = current.Status;
                    AbortCurrent();
                    Stop($"falha: {why}");
                    break;
            }
        }

        // ------------------------------------------------------------------ comer

        /// <summary>Escolhe a comida da barra rápida e "aperta" a tecla dela. false = não há o que comer.</summary>
        private bool TryStartEating(GameSnapshot s)
        {
            if (!settings.AutoEat.Value || eatBroken || Time.unscaledTime < eatRetryAt)
            {
                return false;
            }
            if (eatsInARow >= MaxEatsInARow)
            {
                ModLog.WarnOnce("EatLoop", $"Comeu {MaxEatsInARow} vezes seguidas e a energia não subiu o bastante — parando de comer.");
                return false;
            }
            List<HotBarFood> foods = new List<HotBarFood>();
            foreach (HotBarFood f in GameApi.GetHotBarFoods())
            {
                if (f.Insanity > 0f)
                {
                    if (skippedFoodLogged.Add(f.ItemId))
                    {
                        ModLog.Info($"Comida: pulando {f.ItemId} (aumenta a insanidade em {f.Insanity:0}).");
                    }
                    continue;
                }
                foods.Add(f);
            }
            if (foods.Count == 0)
            {
                return false;
            }

            // Menos desperdício: o maior item que cabe na energia que falta; se nenhum cabe, o menor (só abaixo do limite;
            // completando a energia, nada que passe do máximo).
            float missing = Mathf.Max(0f, s.EnergyMax - s.Energy);
            HotBarFood pick = foods.Where(f => f.Energy <= missing).OrderByDescending(f => f.Energy).FirstOrDefault();
            if (pick.ItemId == null)
            {
                if (s.Energy >= settings.EatBelowEnergy.Value)
                {
                    return false;
                }
                pick = foods.OrderBy(f => f.Energy).First();
            }

            AbortCurrent(); // solta a tecla de ação/movimento; a tarefa retoma depois (a receita fica em andamento)
            eating = true;
            eatingFood = pick;
            eatStartedAt = Time.unscaledTime;
            energyBeforeEat = s.Energy;
            countBeforeEat = pick.Count;
            eatsInARow++;
            eatKeySent = false;
            eatKeyTries = 0;
            State = BotState.Running;
            StateDetail = "comendo";
            ModLog.Info($"Comida: energia {s.Energy:0}/{s.EnergyMax:0} — usando {pick}");
            return true;
        }

        private void TickEating(GameSnapshot s)
        {
            State = BotState.Running;
            StateDetail = "comendo";
            if (!eatKeySent)
            {
                if (Time.unscaledTime - eatStartedAt >= EatKeyDelay)
                {
                    GameApi.PressHotBar(eatingFood.Slot); // = o jogador apertar a tecla 1–4
                    eatKeySent = true;
                    eatKeySentAt = Time.unscaledTime;
                    eatKeyTries++;
                }
                return;
            }
            bool energyUp = s.Energy > energyBeforeEat + 0.5f;
            bool itemUsed = GameApi.CountPlayerItem(eatingFood.ItemId) < countBeforeEat;
            if (energyUp || itemUsed)
            {
                eating = false;
                toppingUp = true;
                ModLog.Info($"Comida: comeu {eatingFood.ItemId} — energia {energyBeforeEat:0} → {s.Energy:0}");
                return;
            }
            if (Time.unscaledTime - eatKeySentAt > EatTimeout)
            {
                if (eatKeyTries < EatKeyAttempts)
                {
                    eatKeySent = false; // tenta a tecla mais uma vez
                    eatStartedAt = Time.unscaledTime;
                    return;
                }
                eating = false;
                toppingUp = false;
                eatsInARow--;
                if (GameApi.IsCarryingAnything())
                {
                    eatRetryAt = Time.unscaledTime + 15f;
                    ModLog.Info($"Comida: a tecla {eatingFood.Slot + 1} não fez efeito com algo nas mãos — tento de novo depois.");
                }
                else
                {
                    eatBroken = true;
                    ModLog.Warn($"Comida: apertei a tecla {eatingFood.Slot + 1} mas nada aconteceu — não vou tentar comer de novo até religar o bot.");
                }
            }
        }

        private void AbortCurrent()
        {
            if (current == null)
            {
                return;
            }
            try
            {
                current.Abort();
            }
            catch (Exception e)
            {
                ModLog.Error($"Erro ao abortar {current.Name}: {e.Message}");
            }
            current = null;
        }

        private static bool SafeCanRun(ITask t, out string reason)
        {
            try
            {
                return t.CanRun(out reason);
            }
            catch (Exception e)
            {
                reason = "erro: " + e.Message;
                ModLog.WarnOnce("CanRun." + t.Name, $"{t.Name}.CanRun falhou: {e}");
                return false;
            }
        }
    }
}
