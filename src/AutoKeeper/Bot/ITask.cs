namespace AutoKeeper.Bot
{
    internal enum TaskResult
    {
        /// <summary>Still working; call Tick again on the next interval.</summary>
        Running,

        /// <summary>Cycle completed successfully (e.g. one body processed).</summary>
        Succeeded,

        /// <summary>Could not continue; the reason goes to the log and the bot stops.</summary>
        Failed,
    }

    /// <summary>
    /// A bot routine (one class per routine in Bot/Tasks). Rules:
    /// - only acts through GameApi, with actions the player could perform;
    /// - Tick is short (no long loops); state lives in the task's own fields;
    /// - Abort must release virtual keys / stop movement and leave the game in a clean state.
    /// </summary>
    internal interface ITask
    {
        string Name { get; }

        /// <summary>Short text of the current step, for the overlay.</summary>
        string Status { get; }

        /// <summary>Is there work to do now? If not, <paramref name="reason"/> explains why (e.g. "no bodies").</summary>
        bool CanRun(out string reason);

        TaskResult Tick();

        /// <summary>Stops safely (kill switch, pause, menu, out of energy).</summary>
        void Abort();

        /// <summary>Forgets everything learned about the world (new save load, back to the menu).</summary>
        void ResetMemory();
    }
}
