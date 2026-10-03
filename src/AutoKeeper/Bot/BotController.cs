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

        /// <summary>
        /// Out of food with low energy and "Sleep, then resume" on: the body task walks to the bed instead of the bot turning
        /// off (sleeping refills energy). Cleared once energy is back above the "eat below" value, or when the bot stops.
        /// </summary>
        public static bool RestRequested { get; private set; }
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
            RestRequested = false;
            Navigator.Reset();
            memoryClean = false;
            State = BotState.Idle;
            StateDetail = Lang.T("aguardando", "waiting");
            ModLog.Info(Lang.T("Bot LIGADO.", "Bot ON."));
            ModLog.ResetOnce("SleepSoon");
            ModLog.ResetOnce("NoFood");
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
            RestRequested = false;
            State = BotState.Off;
            StateDetail = reason;
            ModLog.Info(Lang.T($"Bot DESLIGADO: {reason}", $"Bot OFF: {reason}"));
        }

        public void Update(float unscaledDeltaTime)
        {
            if (State != BotState.Off)
            {
                SessionStats.OnSeconds += unscaledDeltaTime; // "on for 5h02" on the panel's session line
            }
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
            SessionStats.Reset();
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
            if (RestRequested && energyKnown && s.Energy >= settings.EatBelowEnergy.Value)
            {
                RestRequested = false; // slept: energy is back
            }
            if (energyKnown && s.Energy < settings.MinEnergy.Value)
            {
                string why = NoFoodReason();
                float min = settings.MinEnergy.Value;
                int shown = (int)System.Math.Floor(s.Energy); // 9.6 would round to "10 < 10"
                if (settings.OnLackOfSleep.Value != LackOfSleepAction.Sleep || !settings.BodiesEnabled.Value)
                {
                    Stop(Lang.T($"energia baixa ({shown} < {min:0}){why}", $"low energy ({shown} < {min:0}){why}"));
                    return;
                }
                if (!RestRequested)
                {
                    RestRequested = true;
                    AbortCurrent(); // the task replans: finishes placing a carried body, then walks to the bed
                    ModLog.Warn(Lang.T($"Energia baixa ({shown} < {min:0}){why} — vou dormir na cama de casa para recuperar a energia e depois continuo.",
                        $"Low energy ({shown} < {min:0}){why} — going to sleep in the home bed to recover energy, then I'll carry on."));
                }
            }

            // 2b) Health: high insanity lowers max energy and blocks work; lack of sleep turns energy into insanity.
            if (s.Insanity > settings.MaxInsanity.Value)
            {
                // One decimal: 60.1 would read "60 > 60". Sleeping does not help here: the game only removes insanity (-20) when
                // sleep cures Lack of sleep (EnergySystem.RestoreEnergyWhileSleeping, IL 1.008), so the message no longer says "rest".
                Stop(Lang.T($"insanidade alta ({s.Insanity:0.0} > {settings.MaxInsanity.Value:0}) — coma algo que reduza a insanidade (dormir só tira insanidade quando cura a Privação de Sono)",
                    $"high insanity ({s.Insanity:0.0} > {settings.MaxInsanity.Value:0}) — eat something that lowers insanity (sleeping only removes insanity when it cures Lack of sleep)"));
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
                if (onLack == LackOfSleepAction.Sleep)
                {
                    // With "Sleep, then resume" this is the normal cycle, not a problem: a plain event, not a yellow warning (0.3.36).
                    ModLog.InfoOnce("SleepSoon", Lang.T($"Sono: {awake:0.00} dia(s) sem dormir — em 2 dias o jogo aplica a Privação de Sono e o bot vai dormir na cama de casa.",
                        $"Sleep: {awake:0.00} day(s) without sleep — at 2 days the game applies Lack of sleep and the bot will sleep in the home bed."));
                }
                else
                {
                    ModLog.WarnOnce("SleepSoon", Lang.T($"Sono: {awake:0.00} dia(s) sem dormir — em 2 dias o jogo aplica a Privação de Sono e o bot desliga. Durma logo.",
                        $"Sleep: {awake:0.00} day(s) without sleep — at 2 days the game applies Lack of sleep and the bot turns off. Sleep soon."));
                }
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
                ModLog.WarnOnce("NoFood", Lang.T($"Comida: energia {s.Energy:0} e nada para comer na barra de atalhos (teclas 1 a 4). {OutOfFoodPlan()}",
                    $"Food: energy {s.Energy:0} and nothing to eat on the hot bar (keys 1 to 4). {OutOfFoodPlan()}"));
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
                ModLog.ResetOnce("NoFood");
                ReportFoodLeft(eatingFood.ItemId);
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

        /// <summary>Warns when the hot bar food is running out (last units, then none left).</summary>
        private void ReportFoodLeft(string itemId)
        {
            int left = GameApi.CountPlayerItem(itemId);
            bool other = GameApi.GetHotBarFoods().Any(f => f.Insanity <= 0f && f.ItemId != itemId);
            if (left <= 0)
            {
                if (other)
                {
                    ModLog.Info(Lang.T($"Comida: acabou {itemId} — sigo com o resto da barra de atalhos.", $"Food: {itemId} ran out — carrying on with the rest of the hot bar."));
                }
                else
                {
                    ModLog.Warn(Lang.T($"Comida: acabou a comida da barra de atalhos ({itemId} era a última). {OutOfFoodPlan()}",
                        $"Food: the hot bar food ran out ({itemId} was the last one). {OutOfFoodPlan()}"));
                }
            }
            else if (left <= 2 && !other)
            {
                ModLog.Info(Lang.T($"Comida: restam {left} {itemId} na barra de atalhos.", $"Food: {left} {itemId} left on the hot bar."));
            }
        }

        /// <summary>What happens without food: sleep (with "Sleep, then resume") or turn off.</summary>
        private string OutOfFoodPlan()
        {
            float min = settings.MinEnergy.Value;
            return settings.OnLackOfSleep.Value == LackOfSleepAction.Sleep
                ? Lang.T($"Com energia abaixo de {min:0}, vou dormir na cama de casa para recuperar a energia.",
                    $"Below {min:0} energy, I'll sleep in the home bed to recover energy.")
                : Lang.T($"Com energia abaixo de {min:0}, o bot desliga — ponha comida nas teclas 1 a 4.",
                    $"Below {min:0} energy, the bot turns off — put food on keys 1 to 4.");
        }

        /// <summary>Why the bot cannot eat now, as " — …" to append to a message ("" if there is food it would eat).</summary>
        private string NoFoodReason()
        {
            if (!settings.AutoEat.Value)
            {
                return Lang.T(" — \"Comer da barra de atalhos\" está desligado", " — \"Eat from the hot bar\" is off");
            }
            if (eatBroken)
            {
                return Lang.T(" — a tecla da comida não funcionou", " — the food key did not work");
            }
            List<HotBarFood> foods = GameApi.GetHotBarFoods();
            if (foods.Count == 0)
            {
                return Lang.T(" — sem comida na barra de atalhos (teclas 1 a 4)", " — no food on the hot bar (keys 1 to 4)");
            }
            if (foods.All(f => f.Insanity > 0f))
            {
                return Lang.T(" — a comida da barra de atalhos aumenta a insanidade", " — the food on the hot bar raises insanity");
            }
            return "";
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
