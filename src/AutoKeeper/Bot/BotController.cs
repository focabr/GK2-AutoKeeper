using System;
using System.Collections.Generic;
using AutoKeeper.Config;
using AutoKeeper.Core;

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

        public BotState State { get; private set; } = BotState.Off;

        /// <summary>Motivo do estado atual (pausa, ociosidade, desligamento).</summary>
        public string StateDetail { get; private set; } = "desligado";

        /// <summary>Última leitura do jogo (atualizada a cada tick, mesmo com o bot desligado, para o overlay).</summary>
        public GameSnapshot LastSnapshot { get; private set; } = new GameSnapshot();

        public string CurrentTaskText => current == null ? "-" : $"{current.Name}: {current.Status}";

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

            LastSnapshot = StateReader.Read();
            if (State != BotState.Off)
            {
                Tick(LastSnapshot);
            }
        }

        private void Tick(GameSnapshot s)
        {
            // 1) Pausa automática: nunca agir com menu, janela, diálogo ou cinemática.
            if (s.BlockReason != null)
            {
                if (State != BotState.Paused)
                {
                    AbortCurrent(); // solta teclas/movimento; a tarefa replaneja ao voltar
                    ModLog.Debug($"Bot pausado: {s.BlockReason}");
                }
                State = BotState.Paused;
                StateDetail = s.BlockReason;
                return;
            }

            // 2) Fim de ciclo seguro: energia baixa desliga o bot.
            if (s.EnergyMax > 0f && s.Energy >= 0f && s.Energy < settings.MinEnergy.Value)
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
