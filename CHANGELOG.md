# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); versões seguem [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.1.0] - 2026-09-28

### Adicionado
- Estrutura do plugin BepInEx 5 (`com.focabr.gk2.autokeeper`), Harmony com `UnpatchSelf`.
- Configuração (`.cfg`): hotkeys F8 (bot), F9 (overlay), F10 (dump), intervalo de tick, energia mínima.
- Overlay na tela com estado do bot, versão do jogo, motivo de pausa automática, posição, energia, dia/hora e item carregado.
- `GameApi` (adapter único) com leitura de estado protegida contra mudanças do jogo.
- Verificação de compatibilidade com a versão testada do jogo (1.006).
- Dump de descoberta somente leitura (F10) para mapear objetos/itens/receitas da cena.
- `BotController` com fila de tarefas, pausa automática (menu, janela, diálogo, cinemática) e parada por energia baixa. Nenhuma tarefa ainda.
