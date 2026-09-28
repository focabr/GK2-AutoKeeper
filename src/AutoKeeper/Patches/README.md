# Patches Harmony

Convenções deste projeto:

- **Um arquivo por feature** (ex.: `VirtualInputPatch.cs`), com `[HarmonyPatch]` explícito.
- Preferir **Prefix/Postfix**; Transpiler só se não houver alternativa (e documentar o motivo).
- Alvos de métodos privados/por nome via `AccessTools`, com checagem de `null` e log claro
  (use `[HarmonyPrepare]` para pular o patch se o alvo não existir em outra versão do jogo).
- Todos os patches são aplicados por `Plugin` com um único `Harmony` de ID = GUID
  (`com.focabr.gk2.autokeeper`) e removidos com `UnpatchSelf()`.
- Patch nunca altera save nem cria itens; só observa ou injeta "input virtual" equivalente ao do jogador.

Implementado (0.2.0): `VirtualInputPatch` — Postfix em `LazyBearTechnology.LazyInput.Update()` que
acrescenta teclas virtuais do bot (Interaction/Action) às listas `pressedKeys`/`holdedKeys`,
somente enquanto o input do jogo está ativo. Veja `docs/game-api-notes.md`.
