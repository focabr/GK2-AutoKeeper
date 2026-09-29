# GK2 AutoKeeper

Mod de automação (bot) para **Graveyard Keeper 2** — BepInEx 5 + HarmonyX, Unity 6 (Mono).
O bot só executa ações que o jogador poderia fazer; nunca altera o save nem cria itens.

- GUID: `com.focabr.gk2.autokeeper` · Versão: ver `Directory.Build.props` (SemVer)
- Jogo testado: **1.007** (Unity 6000.3.9f1) · BepInEx **5.4.23.5**

## Estado atual
| Etapa | Situação |
|---|---|
| Plugin carrega, loga versão, overlay (F9) | ✅ 0.1.0 |
| StateReader no overlay (posição, energia, dia/hora, item carregado) | ✅ 0.1.0 |
| Dump de descoberta somente leitura (F10) | ✅ 0.1.0 |
| Primeira rotina: **processar corpos** (palete → autópsia → crematório) | ✅ 0.2.0 (testada no necrotério) |
| Tela de configurações no jogo (F11) + menu Mods (GK2 Mod Framework, opcional) | ✅ 0.2.2 |
| Ir sozinho até o trabalho pelas portas, comer da barra rápida, "Outros" da autópsia, maestria | 🧪 0.2.3 |
| Enterro em cova no cemitério | 🔜 0.3 |

## Estrutura
```
src/AutoKeeper/
  Plugin.cs            entrada BepInEx: config, Harmony, hotkeys, ciclo de vida
  Config/Settings.cs   todas as ConfigEntry (hotkeys, toggles, intervalos)
  Core/GameApi*.cs     ÚNICO ponto que toca classes do jogo (adapter)
  Core/StateReader.cs  snapshot do estado para overlay e bot
  Core/ModLog.cs       log BepInEx + buffer para o overlay
  Bot/BotController.cs máquina de estados / fila de tarefas (ticks, pausa automática)
  Bot/ITask.cs         contrato das rotinas (Bot/Tasks/*.cs)
  Patches/             patches Harmony isolados por feature
  UI/Overlay.cs        painel de status na tela (OnGUI)
  UI/SettingsWindow.cs tela de configurações (F11)
src/AutoKeeper.FrameworkBridge/  ponte opcional para o menu Mods do GK2 Mod Framework
docs/game-api-notes.md engenharia reversa + proposta da GameApi
thunderstore/          manifest.json, icon.png, README.md do pacote
build.ps1              build + deploy em BepInEx/plugins + zip de release
```

## Compilar
Pré-requisitos: .NET SDK 8+ (testado com 10), jogo com BepInEx 5.4.23.x instalado.

1. Copie `GamePath.props.example` para `GamePath.props` e ajuste o caminho do jogo
   (ou defina a variável de ambiente `GK2_GAME_PATH`).
2. PowerShell na pasta do projeto:
   ```powershell
   .\build.ps1            # compila (Release) e instala em <jogo>\BepInEx\plugins\AutoKeeper\
   .\build.ps1 -Package   # também gera dist\AutoKeeper-x.y.z.zip (formato Thunderstore)
   ```
   Ou só `dotnet build src/AutoKeeper -c Release`.

> Se o PowerShell bloquear o script: `powershell -ExecutionPolicy Bypass -File .\build.ps1`

## Configurações pela tela do jogo
- **F11** (ou o botão **Configurações** no painel) abre a tela do AutoKeeper: tudo é ajustado ali e salvo sozinho.
- Com o **GK2 Mod Framework** instalado ([Nexus](https://www.nexusmods.com/graveyardkeeper2/mods/42)), as mesmas
  opções aparecem também em **Mods** no menu principal e no menu de pausa (visual nativo, funciona com controle).
- O arquivo `BepInEx/config/com.focabr.gk2.autokeeper.cfg` continua existindo (padrão BepInEx / mod managers),
  mas não precisa ser editado à mão.

## Teclas (configuráveis na tela F11)
| Tecla | Ação |
|---|---|
| F8 | liga/desliga o bot (kill switch) |
| F9 | mostra/esconde overlay |
| F10 | dump de descoberta (JSON) em `BepInEx/config/AutoKeeper/dumps/` |
| F11 | abre/fecha a tela de configurações |

### Bot (`[Bot]` no .cfg)
| Opção | Padrão | O que faz |
|---|---|---|
| `MinEnergy` | `10` | abaixo disso (e sem comida) o bot desliga |
| `AutoEat` | `true` | com energia baixa, usa um item de energia da barra rápida (teclas 1–4), como o jogador; pula itens que aumentam a insanidade |
| `EatBelowEnergy` | `20` | energia em que começa a comer (deixe acima de `MinEnergy`) |
| `UseDoors` | `true` | vai sozinho até onde há trabalho, atravessando portas (casa → pátio → necrotério) pelo caminho mais curto |

### Rotina "Processar corpos" (`[Bodies]` no .cfg)
| Opção | Padrão | O que faz |
|---|---|---|
| `Enabled` | `true` | liga a rotina |
| `Destination` | `Crematorium` | `Crematorium`, `LeaveOnTable` ou `Grave` (experimental) |
| `RequireMastery` | `true` | vale por cima das opções de extração: confere a maestria item por item e pula o que ficar abaixo de `MinMasteryChance` |
| `MinMasteryChance` | `100` | chance mínima (%) mostrada na janela "Remover …" (100 = só com maestria total) |
| `ExtractSkin` … `ExtractGuts` | `true` | quais órgãos extrair (pele, ossos, crânio, coração, cérebro, vísceras) |
| `ExtractFlesh`, `ExtractFat`, `ExtractBlood` | `true` | itens da seção "Outros" da mesa (carne, gordura, sangue) |
| `ExtractOtherPocket` | `false` | qualquer outro item de "Outros" |
| `GraveCraftId` (Avançado) | vazio | força a receita de enterro |
| `SearchRadius` | `80` | alcance para pegar corpos soltos no chão (m); mesas, paletes e crematório são achados em qualquer lugar alcançável |

O bot para sozinho com energia abaixo de `[Bot] MinEnergy` (se não houver comida), se o trabalho não avançar (`WorkStallSeconds`) ou se não conseguir chegar/mirar no alvo — sempre com o motivo no overlay e no log.

## Boas práticas seguidas
- Nada de editar `Assembly-CSharp.dll` em disco: só patches em runtime (Harmony ID = GUID, `UnpatchSelf`).
- DLLs do jogo referenciadas com `Private=false`; nunca redistribuídas (ver `lib/README.md`).
- Bot em ticks (padrão 0,25 s), pausa sozinho em menu/janela/diálogo/cinemática/sono, para com energia baixa.
- Aviso no log se a versão do jogo diferir da testada.
- Código comentado em português, nomes em inglês.

## Licença
MIT — ver `LICENSE`.
