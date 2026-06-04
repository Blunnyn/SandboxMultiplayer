# SandboxMultiplayer — Contexto de Proyecto para Claude

## Stack técnico
- **Motor**: Unity 6.3 (Unity 6000.3.8f1)
- **Red**: Netcode for GameObjects (NGO) + Unity Gaming Services (UGS) para lobbies
- **XR**: XR Interaction Toolkit 3.3.0 (XRI 3.x) — NearFarInteractor, XRGrabInteractable, XRSocketInteractor
- **Target**: Meta Quest 3 (Android XR / OpenXR)
- **Lenguaje**: C# con namespaces `XRMultiplayer`, `XRMultiplayer.SafetyGame`, `UnityEngine.XR.Templates.MRTTabletopAssets`
- **Escena principal**: `Assets/Scenes/SampleScene.unity`

## Arquitectura general
Aplicación de tablero MR multijugador. Una mesa física real se registra en `Vector3.zero` y sobre ella se renderiza contenido virtual a escala reducida.

```
TableSystem (escala mundo)
└── Tabletop Games
    └── SafetyGameEnvironment      ← escala ≈0.0085 (tablero), ACTIVO, tiene el NetworkObject
        ├── SafetyGameMode         ← IGameMode, NetworkBehaviour, gestiona el minijuego
        ├── SafetyGameManager      ← singleton NetworkBehaviour — DEBE vivir aquí (objeto activo)
        ├── Cityconstruction       ← escenario visual (COMIENZA DESACTIVADO en runtime)
        │   └── waypoints, sockets, zonas...
        ├── ConeSocketsContainer   ← sockets de colocación de conos
        ├── BarrierSocketsContainer← sockets de colocación de barreras
        ├── ConeDispenser / BarrierDispenser
        ├── SafetyIntroUI          ← UI local del escenario (NO es NetworkObject); ver "UI del escenario"
        └── WorkerNPC (spawneado dinámicamente en runtime)
```

### Escala del tablero
`SafetyGameEnvironment.localScale ≈ 0.0085`. **Todo objeto que sea hijo hereda esta escala.** Los prefabs de red (conos, barreras, WorkerNPC) deben tener `localScale = Vector3.one` al spawnearse como hijos de `SafetyGameEnvironment`.

### Regla crítica de NGO (¡importante!)
Los `NetworkBehaviour`/`NetworkObject` dentro de un GameObject **desactivado** al momento del spawn de NGO son **excluidos del spawn** y nunca se sincronizan (`IsSpawned = false`). Síntoma en consola:
`[X][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!`
y al escribir una NetworkVariable: `NetworkVariable is written to, but doesn't know its NetworkBehaviour yet`.

Consecuencias:
- `Cityconstruction` comienza desactivado → cualquier objeto de red que deba replicarse **no** puede ser in-scene dentro de él; debe spawnarse dinámicamente.
- **`SafetyGameManager` DEBE estar en `SafetyGameEnvironment` (objeto activo con el NetworkObject), NUNCA dentro de `Cityconstruction`.** Si se coloca en un objeto desactivado, nunca spawnea y sus NetworkVariables (score, objetivos…) no se sincronizan ni se inicializan.

## ⚠️ Dos prefabs de entorno (fuente de confusión conocida)
Existen **dos** prefabs casi idénticos:
- `Assets/MRTabletopAssets/Prefabs/GameModes/SafetyGameEnvironment.prefab`
- `Assets/MRTabletopAssets/Prefabs/GameModes/SafetyGameEnvironment 1.prefab` (variante editada más recientemente)

Editar el campo de un prefab no afecta al otro ni necesariamente a la instancia de la escena. Si un valor "no se aplica" en runtime, verificar **cuál prefab/instancia usa realmente `SampleScene`** y editar ese. Pendiente: decidir cuál es el canónico y eliminar el otro.

## Modo de juego de seguridad (Game Mode ID 5)

### Scripts principales
| Script | Ruta | Responsabilidad |
|--------|------|-----------------|
| `SafetyGameMode` | `Games/SafetyGame/Scripts/` | IGameMode — inicia/termina el juego, spawna WorkerNPC, gestiona dispensers y la UI local (`m_IntroUI`) |
| `SafetyGameManager` | `Games/SafetyGame/Scripts/` | Singleton NetworkBehaviour — puntuación, tiempo, **objetivos (conos/barreras)**, registro de NPCs, zonas y sockets |
| `SafetyNPC` | `Games/SafetyGame/Scripts/` | Trabajador NPC — movimiento por waypoints, alerta on-hover, UI de alerta replicada, anti-spam |
| `SafetyObjectSocket` | `Games/SafetyGame/Scripts/` | NetworkBehaviour sobre XRSocketInteractor — filtra por tag, notifica HazardZone y reporta objetivos al manager |
| `SafetyHazardZone` | `Games/SafetyGame/Scripts/` | Zona de peligro con trigger — penaliza si el NPC entra sin suficientes objetos colocados |
| `SafetyObjectivesUI` | `Games/SafetyGame/Scripts/` | **MonoBehaviour de presentación** — lee las NetworkVariables de objetivos del manager y actualiza textos TMP |

### Flujo de spawn del WorkerNPC
1. `SafetyGameMode.OnGameModeStart()` → solo en servidor
2. `SpawnWorker()`: `Instantiate(m_WorkerNpcPrefab, transform)` — hijo de `SafetyGameEnvironment`
3. `worker.transform.localScale = Vector3.one`
4. `worker.Spawn(true)` — NGO spawna en todos los clientes
5. `npc.InitializeRoute(m_WorkerWaypoints)` — waypoints asignados en runtime (no serializables en prefab)

### NetworkVariables del SafetyNPC
Todos con `WritePermission.Server`:
- `isInDanger`, `isAlerted` — estado del NPC
- `m_NetPosition`, `m_NetRotationY`, `m_NetIsWalking` — sincronización de transform manual
- `m_NetUIVisible` — activa/desactiva `m_AlertUIContent` en todos los clientes

El servidor mueve el NPC y escribe las NetworkVariables cada frame. Los clientes leen y lerp hacia los valores de red.

### Sistema anti-spam de la alerta del NPC (sesión 2026-06)
Al tocar al NPC con el mando: se detiene, muestra la imagen de alerta y suena un audio. Se corrigió el spam:
- **`m_IsLocalAlertPending`** (bool local): se activa en el instante de pedir la alerta y bloquea nuevas peticiones hasta que termina el ciclo. Evita que un cliente envíe decenas de `AlertNPCServerRpc()` antes de que `isAlerted` se propague. Se libera en `m_NetUIVisible.OnValueChanged` cuando la UI se oculta.
- **El sonido se reproduce desde código**, no desde eventos de hover. Campo `m_AlertAudioSource` (con fallback `GetComponent<AudioSource>()` en `Awake`). Se llama `PlayAlertSound()` en `m_NetUIVisible.OnValueChanged` cuando pasa a visible → un solo sonido por ciclo.
- **Tiempos de la imagen**: `m_NetUIVisible` pasa a `false` cuando el NPC reanuda la marcha (fin de `m_AlertDuration`), **no** al final del cooldown. La imagen se ve solo durante `m_AlertDuration`; el `m_AlertCooldown` sigue bloqueando re-alertas en silencio (sin imagen).
- **Setup en el prefab WorkerNPC (manual en editor):** en el `XRSimpleInteractable` hay que **quitar el AudioClip de hover** y **limpiar los UnityEvents** de `Last Hover Exited`; `First Hover Entered` debe llamar SOLO a `SafetyNPC.AlertNPC()`. Nada externo debe tocar `m_AlertUIContent` — lo controla únicamente la NetworkVariable.

### Sistema de conos y barreras
- **Prefab de cono**: `SafetyCone Variant` (en `Assets/Samples/`) — variante de `SM_Prop_Cone_01`, tag `SafetyCone`
- **Prefab de barrera**: `RoadBlock` (ya existe; instancias `RoadBlock(Clone)`), tag `SafetyBarrier`
- Ambos llevan: `Rigidbody` + `BoxCollider`/`MeshCollider` + `XRGrabInteractable` + `XRGeneralGrabTransformer` + `NetworkObject` + `ClientNetworkTransform` + `NetworkPhysicsInteractable`
- Se registran en `_SafetyGamePrefabList` (en `NetworkedPrefabs/`) y se spawnan via `NetworkObjectDispenser`
- Los sockets de colocación usan `XRSocketInteractor` + `SafetyObjectSocket` (filtra por tag). Están en `ConeSocketsContainer` / `BarrierSocketsContainer`.

### Sistema de objetivos (conos/barreras restantes) — sesión 2026-06
Muestra cuántos conos y barreras se han colocado de los requeridos.

**`SafetyGameManager`** — NetworkVariables nuevas (todas `ReadPermission.Everyone`, `WritePermission.Owner`, igual que `score`/`timeRemaining`):
- `conesPlaced`, `conesRequired`, `barriersPlaced`, `barriersRequired`
- Campos serializados que definen el OBJETIVO: `m_ConesRequired`, `m_BarriersRequired`.
- En `OnNetworkSpawn` (servidor): `conesRequired.Value = m_ConesRequired;` (idem barreras) y `RecountPlacedObjects()`.
- `RegisterObjectSocket(socket)` + lista `m_RegisteredSockets`.
- `RecountPlacedObjects()` (server-only): recorre los sockets registrados y cuenta los ocupados por tag (`k_ConeTag="SafetyCone"`, `k_BarrierTag="SafetyBarrier"`), escribe `conesPlaced`/`barriersPlaced`.

**`SafetyObjectSocket`** — añadidos:
- `OnNetworkSpawn` → `SafetyGameManager.Instance.RegisterObjectSocket(this)`.
- `public string ObjectTag => m_ObjectTag;`
- `public bool IsOccupiedByValidObject()` — true si el socket tiene un objeto del tag esperado.
- En colocar/quitar: además de `NotifyHazardZone()`, llama `NotifyObjectivesManager()` → recuento server-authoritative (con `RecountObjectivesServerRpc()` como fallback si lo dispara un cliente).

**`SafetyObjectivesUI`** (MonoBehaviour, NO de red):
- Espera en corrutina `BindWhenReady()` a que `SafetyGameManager.Instance != null && IsSpawned`, luego se suscribe a los `OnValueChanged` de las 4 NetworkVariables y refresca.
- Muestra `"Conos: X/Y"` y `"Barreras: X/Y"` (formato configurable). `m_CompletedIndicator` opcional se activa cuando todo está colocado.

**⚠️ Gotchas de los objetivos (causaron depuración larga):**
- El texto lee `conesRequired.Value`, que **se sobrescribe en `OnNetworkSpawn` con `m_ConesRequired`**. Editar la NetworkVariable expuesta en el Inspector NO sirve; hay que editar el **campo `m_ConesRequired`** (header "Objetivos").
- `conesRequired`/`barriersRequired` son **números manuales**, NO cuentan sockets. Solo `conesPlaced`/`barriersPlaced` cuentan objetos reales.
- Estado actual en `SampleScene`: `m_ConesRequired = 4`, `m_BarriersRequired = 2` (puestos en el `SafetyGameManager` de `SafetyGameEnvironment`).

### Dispensers
- `coneDispenser` — spawna conos (`SafetyCone Variant`)
- `barrierDispenser` — spawna barreras (`RoadBlock`)
- Ambos referenciados en `SafetyGameMode` y llamados en `ShowGameMode`/`HideGameMode`
- El prefab a spawnar se asigna en el **child** `InteractableSpawner` → campo `Spawn Interactable Prefab` (tipo `NetworkBaseInteractable`)
- `InteractableSpawner.SpawnInteractablePrefab()` sobreescribe el `localScale` del objeto spawneado con el `lossyScale` del spawn transform → la escala del tablero se aplica automáticamente

## Componentes UI reutilizables (en `Scripts/UI/`, namespace `XRMultiplayer`) — sesión 2026-06
Pensados para Canvas World Space, reutilizables en cualquier UI:

- **`FloatingBillboard`** — billboard suave (mira a la cámara con `Slerp`) + efecto flotante sinusoidal vertical. Bools independientes `m_FaceCamera` / `m_FloatEffect` (+ `SetFaceCamera`/`SetFloatEffect`). Bloqueo de ejes `m_LockX/Y/Z`: **Lock X aplana la dirección (`dir.y=0`) ANTES del `LookRotation`**; Lock Y/Z reemplazan el euler del resultado (los euler de Unity son interdependientes — no se puede bloquear X reemplazando el euler resultante). Captura la rotación inicial en `Start()` (`m_LockedEuler`).
- **`GrabActivatedUI`** — activa/desactiva una UI cuando el jugador agarra un objeto de cierto tag. Va en el **Canvas** (no en el Image); `m_UITarget` apunta al Image hijo. Se suscribe a `interactableRegistered`/`interactableUnregistered` del `XRInteractionManager` y a `selectEntered`/`selectExited` de cada interactable filtrado por tag. Contador `m_ActiveGrabCount` para multijugador.
- **`HeadLockedUI`** — head-lock real: fija el objeto a la cámara cada frame en `Application.onBeforeRender` (sin el lag/"swim" de `LazyFollow`). `m_Offset` en espacio local de cámara, `m_RotationOffset`, `m_Smoothing` (0 = pegado perfecto). **Reemplaza a `LazyFollow`** (no usar ambos a la vez).

### Notas de XRI 3.3.0 (aprendido en esta sesión)
- `XRInteractionManager.interactableRegistered` / `interactableUnregistered` son **C# events nativos** → usar `+=` / `-=`.
- `IXRSelectInteractable.selectEntered` / `selectExited` son **UnityEvents** → usar `AddListener` / `RemoveListener`.
- `XRInteractionManager.interactableSelectEntered/Exited` **NO existen** en esta versión.
- Tipos en namespace `UnityEngine.XR.Interaction.Toolkit.Interactables` (p.ej. `IXRSelectInteractable`, `IXRInteractable`).

## UI del escenario (Coaching UI + HUD de objetivos) — sesión 2026-06
Toda esta UI cuelga de `SafetyGameEnvironment/SafetyIntroUI/Canvas` y se activa/desactiva localmente vía `SafetyGameMode.m_IntroUI` en `ShowGameMode`/`HideGameMode`. **No es NetworkObject**: como `GameModeManager` llama `ShowGameMode()` en TODOS los clientes y `SafetyIntroUI` es un GameObject de escena, cada cliente activa su propia copia (no requiere RPC). Por eso aparece para todos los jugadores.

- **`Coaching UI`** (intro/descripción): instancia del prefab `CoachingUI.prefab` (`Prefabs/UIPrefabs/`). Usa `GoalManager` (pasos con botón Continuar/Skip) + `LazyFollow`. Estructura: `CoachingCardRoot > Card 1..4`, cada Card tiene `Mask Background/Background` (panel redondeado = un `Image`) + `Modal Text` (TMP). Contiene la descripción del escenario en 4 cards. Se cierra al terminar los pasos.
- **`Objectives UI`** (HUD persistente): hermano de `Coaching UI` (vida distinta — persiste durante la partida). Se construyó **duplicando `Coaching UI` y reduciéndolo** (así hereda Canvas + escala compensada del tablero) a un panel con dos TMP. Lleva el componente `SafetyObjectivesUI` (textos `Text_Conos`/`Text_Barreras` asignados) y `HeadLockedUI` en vez de `LazyFollow` para quedar pegado a la vista (con `m_Offset` a una esquina).

> Nota de escala: un Canvas World Space hijo de `SafetyGameEnvironment` heredaría escala ≈0.0085 (microscópico). El `CoachingUI` ya compensa esto en su Canvas interno; por eso se recomienda **duplicar una pieza existente del CoachingUI** en vez de crear un Canvas desde cero.

## Sistema de game modes
`GameModeManager` ordena por `gameModeID` los hijos que implementen `IGameMode` y llama `ShowGameMode`/`HideGameMode` **en todos los clientes localmente**. El Safety Game tiene ID 5.

`IGameMode` requiere: `gameModeID`, `ShowGameMode()`, `HideGameMode()`.

## Interacción XR (NearFarInteractor)
- El interactor usa `SphereInteractionCaster` (near) con radio ~3.5cm
- Solo detecta objetos en **layer Default** con **physics non-triggers**
- El hover del NPC se dispara via `XRSimpleInteractable.firstHoverEntered` → `SafetyNPC.AlertNPC()`
- Hay cooldown de `m_AlertCooldown` (3s por defecto) para evitar spam de sonido/alerta

### Testing con XR Interaction Simulator (no Device Simulator)
Controles en el editor (Play): `WASD` mover, `Q/E` bajar/subir, click derecho (hold) mirar. Manos: `[` activa mano izquierda, `]` derecha, mover ratón mueve el controlador activo. Para **agarrar**: ` ` ` ` (backquote) cicla la Quick Action hasta "Grip", `Space` ejecuta/suelta. `X` menú de acciones, `Y` menú de selección de input.

## Prefabs de red registrados
- `_SafetyGamePrefabList` — WorkerNPC, RoadBlock (barreras)
- `_ConeGamePrefabList` — conos

## Paths clave
```
Assets/MRTabletopAssets/Games/SafetyGame/Scripts/   ← scripts del minijuego (incl. SafetyObjectivesUI)
Assets/MRTabletopAssets/Scripts/UI/                  ← FloatingBillboard, GrabActivatedUI, HeadLockedUI
Assets/MRTabletopAssets/Scripts/GameModes/           ← GameModeManager, IGameMode, GoalManager
Assets/MRTabletopAssets/Scripts/ObjectDispenser/     ← NetworkObjectDispenser, InteractableSpawner
Assets/MRTabletopAssets/Prefabs/GameModes/           ← SafetyGameEnvironment.prefab (+ "1" variante)
Assets/MRTabletopAssets/Prefabs/UIPrefabs/           ← CoachingUI.prefab, Dispenser Panel, etc.
Assets/MRTabletopAssets/Prefabs/GameModes/CityconstructionGame/NetworkedPrefabs/  ← prefab lists
Assets/Samples/                                      ← SafetyCone Variant.prefab
Assets/PolygonConstruction/Prefabs/Props/            ← meshes base (SM_Prop_*)
Assets/MRTabletopAssets/Scripts/Debug/               ← NearCasterGizmo
Assets/Scenes/SampleScene.unity                      ← escena principal
```

## Convenciones del proyecto
- Campos serializados: prefijo `m_` (ej. `m_WorkerNpcPrefab`)
- NetworkVariables: por defecto `ReadPermission.Everyone`, `WritePermission.Server`. **Excepción:** `SafetyGameManager` usa `WritePermission.Owner` (sus métodos guardan con `if (!IsServer) return;` y el host es owner de los objetos de escena).
- Sin `NetworkTransform` para el NPC — se usa sincronización manual via NetworkVariables (evita conflictos con la escala del tablero)
- Gizmos de waypoints: `OnDrawGizmosSelected` en `SafetyGameMode` (se ven al seleccionar `SafetyGameEnvironment`)
- UI de presentación (HUD/objetivos) = MonoBehaviour que LEE NetworkVariables; nunca escribe estado de red.

## Resumen de cambios de la sesión 2026-06 (changelog)
- **Nuevos scripts**: `FloatingBillboard`, `GrabActivatedUI`, `HeadLockedUI` (en `Scripts/UI/`), `SafetyObjectivesUI` (en `Games/SafetyGame/Scripts/`).
- **`SafetyNPC`**: anti-spam (`m_IsLocalAlertPending`), sonido por código (`m_AlertAudioSource` + `PlayAlertSound`), imagen visible solo durante `m_AlertDuration`.
- **`SafetyGameManager`**: sistema de objetivos (4 NetworkVariables + `m_ConesRequired`/`m_BarriersRequired` + `RegisterObjectSocket`/`RecountPlacedObjects`).
- **`SafetyObjectSocket`**: registro en el manager, `ObjectTag`, `IsOccupiedByValidObject`, notificación de recuento.
- **`SafetyGameMode`**: campo `m_IntroUI` activado/desactivado en `ShowGameMode`/`HideGameMode`.
- **Editor**: montada la UI del escenario (Coaching UI con 4 cards de descripción + Objectives UI HUD). Puesto `m_ConesRequired=4`, `m_BarriersRequired=2` en `SampleScene`.
- **Pendientes**: resolver el doble prefab `SafetyGameEnvironment` / `SafetyGameEnvironment 1`; quitar audio/UnityEvents de hover sobrantes en el prefab WorkerNPC; ajustar offset del `HeadLockedUI`; guardar la escena para persistir el valor de conos.
```
