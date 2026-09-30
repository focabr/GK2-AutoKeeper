# Harmony patches

Conventions of this project:

- **One file per feature** (e.g. `VirtualInputPatch.cs`), with an explicit `[HarmonyPatch]`.
- Prefer **Prefix/Postfix**; Transpiler only if there is no alternative (and document why).
- Private/by-name method targets via `AccessTools`, with a `null` check and a clear log
  (use `[HarmonyPrepare]` to skip the patch if the target does not exist in another game version).
- All patches are applied by `Plugin` with a single `Harmony` whose ID = GUID
  (`com.focabr.gk2.autokeeper`) and removed with `UnpatchSelf()`.
- A patch never changes the save nor creates items; it only observes or injects "virtual input" equivalent to the player's.

Implemented (0.2.0): `VirtualInputPatch` — Postfix on `LazyBearTechnology.LazyInput.Update()` that
adds the bot's virtual keys (Interaction/Action) to the `pressedKeys`/`holdedKeys` lists,
only while the game's input is active. See `docs/game-api-notes.md`.
