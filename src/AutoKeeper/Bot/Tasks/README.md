# Bot/Tasks

One class per routine, implementing `ITask` (`CanRun`, `Tick`, `Abort`).

- `ProcessBodiesTask` (0.2.0) — pallet/ground → autopsy table → extract organs → crematorium
  (or leave on the table / experimental grave). Flow and real ids in `docs/game-api-notes.md` (sections 7b and 11).

Planned: burial by going through the morgue door (0.3).
