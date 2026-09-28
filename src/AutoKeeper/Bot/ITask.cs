namespace AutoKeeper.Bot
{
    internal enum TaskResult
    {
        /// <summary>Ainda trabalhando; chame Tick de novo no próximo intervalo.</summary>
        Running,

        /// <summary>Ciclo concluído com sucesso (ex.: um corpo processado).</summary>
        Succeeded,

        /// <summary>Não foi possível continuar; o motivo vai para o log e o bot para.</summary>
        Failed,
    }

    /// <summary>
    /// Uma rotina do bot (uma classe por rotina em Bot/Tasks). Regras:
    /// - só age através da GameApi, com ações que o jogador poderia fazer;
    /// - Tick é curto (nada de loops longos); o estado fica em campos da própria tarefa;
    /// - Abort deve soltar teclas virtuais / parar movimento e deixar o jogo em estado limpo.
    /// </summary>
    internal interface ITask
    {
        string Name { get; }

        /// <summary>Texto curto do passo atual, para o overlay.</summary>
        string Status { get; }

        /// <summary>Há trabalho a fazer agora? Se não, <paramref name="reason"/> explica (ex.: "sem corpos").</summary>
        bool CanRun(out string reason);

        TaskResult Tick();

        /// <summary>Interrompe com segurança (kill switch, pausa, menu, falta de energia).</summary>
        void Abort();
    }
}
