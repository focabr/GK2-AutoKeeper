using System;
using System.Collections.Generic;
using System.Linq;
using AutoKeeper.Config;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.Bot
{
    /// <summary>
    /// State machine + task queue. Runs in "ticks" (configurable interval), never every frame.
    /// Pauses by itself on menu/pause/window/dialogue/cutscene and turns off on low energy or error.
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

        // Eat from the hot bar (low energy).
        private const float EatTimeout = 3f;
        private const float EatKeyDelay = 0.6f;     // waits for the character to leave the work state before pressing the key
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
        private bool eatBroken;             // the key had no effect: does not try again until the bot is turned back on
        private float eatRetryAt;           // carrying something, the key may not work: tries again later
        private bool toppingUp;             // already ate during this stop: keeps going while the food fits whole into the energy
        private readonly HashSet<string> skippedFoodLogged = new HashSet<string>();
        private bool memoryClean;           // memory already cleared and the bot has not run since (avoids repeated warnings)

        public BotState State { get; private set; } = BotState.Off;

        /// <summary>Reason for the current state (pause, idle, shutdown).</summary>
        public string StateDetail { get; private set; } = "";

        /// <summary>Latest game reading (updated every tick, even with the bot off, for the overlay).</summary>
        public GameSnapshot LastSnapshot { get; private set; } = new GameSnapshot();

        public string CurrentTaskText => eating ? Lang.T($"comendo {eatingFood.ItemId}", $"eating {eatingFood.ItemId}") : current == null ? "-" : $"{current.Name}: {current.Status}";

        /// <summary>Only the current step (for the status panel): "eating X", the task status or null.</summary>
        public string CurrentStepText => eating ? Lang.T($"comendo {eatingFood.ItemId}", $"eating {eatingFood.ItemId}") : current?.Status;

        /// <summary>Routes between areas (doors), shared by the tasks.</summary>
        public Navigator Navigator { get; } = new Navigator();

        public BotController(Settings settings)
        {
            this.settings = settings;
        }

        /// <summary>Adds a routine to the queue (order = priority).</summary>
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
                Stop(Lang.T($"desligado pelo jogador ({settings.ToggleBotKey.Value})", $"turned off by the player ({settings.ToggleBotKey.Value})"));
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
            StateDetail = Lang.T("aguardando", "waiting");
            ModLog.Info(Lang.T("Bot LIGADO.", "Bot ON."));
            ModLog.ResetOnce("SleepSoon");
            if (tasks.Count == 0)
            {
                ModLog.Info(Lang.T("Nenhuma tarefa registrada ainda — o bot só monitora o estado.", "No task registered yet — the bot only monitors the state."));
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
            ModLog.Info(Lang.T($"Bot DESLIGADO: {reason}", $"Bot OFF: {reason}"));
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

        /// <summary>New save load (or back to the menu): turns off the bot and clears all internal memory.</summary>
        private void ResetForNewWorld(string why)
        {
            Stop(Lang.T($"{why} — memória do bot zerada", $"{why} — bot memory cleared"));
            foreach (ITask t in tasks)
            {
                try { t.ResetMemory(); } catch (System.Exception e) { ModLog.Warn(Lang.T($"Falha ao zerar {t.Name}: {e.Message}", $"Failed to reset {t.Name}: {e.Message}")); }
            }
            current = null;
            Navigator.Reset();
            skippedFoodLogged.Clear();
            eatBroken = false;
            eatsInARow = 0;
            toppingUp = false;
            // Menu → Continue fires up to 3 events in a row (menu, PlayerData swap, game loaded): clears on
            // all of them, but only reports once until the bot has run again.
            string msg = Lang.T($"Memória do bot zerada ({why}).", $"Bot memory cleared ({why}).");
            if (memoryClean)
            {
                ModLog.Debug(msg + Lang.T(" (já estava zerada)", " (already cleared)"));
                return;
            }
            memoryClean = true;
            ModLog.Info(msg + Lang.T(" Ligue de novo com a tecla do bot quando quiser.", " Turn it back on with the bot key whenever you want."));
        }

        private void Tick(GameSnapshot s)
        {
            // 1) Auto-pause: never act with a menu, window, dialogue or cutscene open.
            if (s.BlockReason != null)
            {
                if (State != BotState.Paused)
                {
                    AbortCurrent(); // releases keys/movement; the task replans on return
                    eating = false;
                    ModLog.Debug(Lang.T($"Bot pausado: {s.BlockReason}", $"Bot paused: {s.BlockReason}"));
                }
                State = BotState.Paused;
                StateDetail = s.BlockReason;
                return;
            }

            // 2) Low energy: eats from the hot bar (if enabled); with no food, turns off safely.
            if (eating)
            {
                TickEating(s);
                return;
            }
            bool energyKnown = s.EnergyMax > 0f && s.Energy >= 0f;
            // A single stop to eat: below the limit it eats and keeps eating while the food fits whole into the energy
            // still missing (e.g. 19 → 49 → 79 of 86), instead of interrupting work at every extraction.
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
                Stop(Lang.T($"energia baixa ({s.Energy:0} < {settings.MinEnergy.Value:0})", $"low energy ({s.Energy:0} < {settings.MinEnergy.Value:0})"));
                return;
            }

            // 2b) Health: high insanity lowers max energy and blocks work; lack of sleep turns energy into insanity.
            if (s.Insanity > settings.MaxInsanity.Value)
            {
                Stop(Lang.T($"insanidade alta ({s.Insanity:0} > {settings.MaxInsanity.Value:0}) — coma algo que reduza a insanidade ou descanse",
                    $"high insanity ({s.Insanity:0} > {settings.MaxInsanity.Value:0}) — eat something that lowers insanity or rest"));
                return;
            }
            // "When Lack of sleep hits": Turn off → stops here; Sleep, then resume → the task walks to the bed; Keep working → only the insanity limit.
            LackOfSleepAction onLack = settings.OnLackOfSleep.Value;
            if (onLack == LackOfSleepAction.Stop && GameApi.HasLackOfSleep())
            {
                Stop(Lang.T("Privação de Sono (2 dias sem dormir): metade da energia gasta vira insanidade — durma na cama até encher a energia",
                    "Lack of sleep (2 days awake): half of the energy you spend turns into insanity — sleep in a bed until your energy is full"));
                return;
            }
            float awake = GameApi.GetDaysWithoutSleep();
            if (onLack != LackOfSleepAction.KeepWorking && awake >= 1.75f && awake < 2f)
            {
                ModLog.WarnOnce("SleepSoon", onLack == LackOfSleepAction.Sleep
                    ? Lang.T($"Sono: {awake:0.00} dia(s) sem dormir — em 2 dias o jogo aplica a Privação de Sono e o bot vai dormir na cama de casa.",
                        $"Sleep: {awake:0.00} day(s) without sleep — at 2 days the game applies Lack of sleep and the bot will sleep in the home bed.")
                    : Lang.T($"Sono: {awake:0.00} dia(s) sem dormir — em 2 dias o jogo aplica a Privação de Sono e o bot desliga. Durma logo.",
                        $"Sleep: {awake:0.00} day(s) without sleep — at 2 days the game applies Lack of sleep and the bot turns off. Sleep soon."));
            }
            else if (awake >= 0f && awake < 1.75f)
            {
                ModLog.ResetOnce("SleepSoon"); // slept: the warning applies again in the next cycle
            }

            // 3) Picks the first task that can run.
            if (current == null)
            {
                string lastReason = tasks.Count == 0 ? Lang.T("nenhuma tarefa implementada ainda", "no task implemented yet") : Lang.T("nada a fazer", "nothing to do");
                foreach (ITask t in tasks)
                {
                    if (SafeCanRun(t, out string reason))
                    {
                        current = t;
                        ModLog.Detail(Lang.T($"Iniciando tarefa: {t.Name}", $"Starting task: {t.Name}"));
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

            // 4) Advances the task one step.
            State = BotState.Running;
            StateDetail = current.Name;
            TaskResult result;
            try
            {
                result = current.Tick();
            }
            catch (Exception e)
            {
                ModLog.Error(Lang.T($"Erro na tarefa {current.Name}: {e}", $"Error in task {current.Name}: {e}"));
                Stop(Lang.T($"erro na tarefa {current.Name}", $"error in task {current.Name}"));
                return;
            }

            switch (result)
            {
                case TaskResult.Succeeded:
                    ModLog.Detail(Lang.T($"Tarefa concluída: {current.Name}", $"Task completed: {current.Name}"));
                    current = null;
                    break;
                case TaskResult.Failed:
                    string why = current.Status;
                    AbortCurrent();
                    Stop(Lang.T($"falha: {why}", $"failed: {why}"));
                    break;
            }
        }

        // ------------------------------------------------------------------ eating

        /// <summary>Picks the food from the hot bar and "presses" its key. false = nothing to eat.</summary>
        private bool TryStartEating(GameSnapshot s)
        {
            if (!settings.AutoEat.Value || eatBroken || Time.unscaledTime < eatRetryAt)
            {
                return false;
            }
            if (eatsInARow >= MaxEatsInARow)
            {
                ModLog.WarnOnce("EatLoop", Lang.T($"Comeu {MaxEatsInARow} vezes seguidas e a energia não subiu o bastante — parando de comer.",
                    $"Ate {MaxEatsInARow} times in a row and energy did not rise enough — stopping eating."));
                return false;
            }
            List<HotBarFood> foods = new List<HotBarFood>();
            foreach (HotBarFood f in GameApi.GetHotBarFoods())
            {
                if (f.Insanity > 0f)
                {
                    if (skippedFoodLogged.Add(f.ItemId))
                    {
                        ModLog.Info(Lang.T($"Comida: pulando {f.ItemId} (aumenta a insanidade em {f.Insanity:0}).", $"Food: skipping {f.ItemId} (raises insanity by {f.Insanity:0})."));
                    }
                    continue;
                }
                foods.Add(f);
            }
            if (foods.Count == 0)
            {
                return false;
            }

            // Less waste: the biggest item that fits in the missing energy; if none fits, the smallest (only below the limit;
            // when topping up the energy, nothing that goes over the maximum).
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

            AbortCurrent(); // releases the action/movement key; the task resumes later (the craft stays in progress)
            eating = true;
            eatingFood = pick;
            eatStartedAt = Time.unscaledTime;
            energyBeforeEat = s.Energy;
            countBeforeEat = pick.Count;
            eatsInARow++;
            eatKeySent = false;
            eatKeyTries = 0;
            State = BotState.Running;
            StateDetail = Lang.T("comendo", "eating");
            ModLog.Detail(Lang.T($"Comida: energia {s.Energy:0}/{s.EnergyMax:0} — usando {pick}", $"Food: energy {s.Energy:0}/{s.EnergyMax:0} — using {pick}"));
            return true;
        }

        private void TickEating(GameSnapshot s)
        {
            State = BotState.Running;
            StateDetail = Lang.T("comendo", "eating");
            if (!eatKeySent)
            {
                if (Time.unscaledTime - eatStartedAt >= EatKeyDelay)
                {
                    GameApi.PressHotBar(eatingFood.Slot); // = the player pressing key 1–4
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
                ModLog.Info(Lang.T($"Comida: comeu {eatingFood.ItemId} — energia {energyBeforeEat:0} → {s.Energy:0}", $"Food: ate {eatingFood.ItemId} — energy {energyBeforeEat:0} → {s.Energy:0}"));
                return;
            }
            if (Time.unscaledTime - eatKeySentAt > EatTimeout)
            {
                if (eatKeyTries < EatKeyAttempts)
                {
                    eatKeySent = false; // tries the key once more
                    eatStartedAt = Time.unscaledTime;
                    return;
                }
                eating = false;
                toppingUp = false;
                eatsInARow--;
                if (GameApi.IsCarryingAnything())
                {
                    eatRetryAt = Time.unscaledTime + 15f;
                    ModLog.Info(Lang.T($"Comida: a tecla {eatingFood.Slot + 1} não fez efeito com algo nas mãos — tento de novo depois.",
                        $"Food: key {eatingFood.Slot + 1} had no effect while carrying something — will try again later."));
                }
                else
                {
                    eatBroken = true;
                    ModLog.Warn(Lang.T($"Comida: apertei a tecla {eatingFood.Slot + 1} mas nada aconteceu — não vou tentar comer de novo até religar o bot.",
                        $"Food: pressed key {eatingFood.Slot + 1} but nothing happened — won't try to eat again until the bot is turned back on."));
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
                ModLog.Error(Lang.T($"Erro ao abortar {current.Name}: {e.Message}", $"Error aborting {current.Name}: {e.Message}"));
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
                reason = Lang.T("erro: ", "error: ") + e.Message;
                ModLog.WarnOnce("CanRun." + t.Name, Lang.T($"{t.Name}.CanRun falhou: {e}", $"{t.Name}.CanRun failed: {e}"));
                return false;
            }
        }
    }
}
