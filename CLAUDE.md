# SandboxMultiplayer — Contexto de Proyecto para Claude

## Stack técnico
- **Motor**: Unity 6.3 (Unity 6000.3.x)
- **Red**: Netcode for GameObjects (NGO) + Unity Gaming Services (UGS) para lobbies
- **XR**: XR Interaction Toolkit 3.x (XRI 3.x) — NearFarInteractor, XRGrabInteractable, XRSocketInteractor
- **Target**: Meta Quest 3 (Android XR / OpenXR)
- **Lenguaje**: C# con namespaces `XRMultiplayer`, `XRMultiplayer.SafetyGame`, `UnityEngine.XR.Templates.MRTTabletopAssets`

## Arquitectura general
Aplicación de tablero MR multijugador. Una mesa física real se registra en `Vector3.zero` y sobre ella se renderiza contenido virtual a escala reducida.

```
TableSystem (escala mundo)
└── Tabletop Games
    └── SafetyGameEnvironment      ← escala 0.0085 (tablero)
        ├── SafetyGameMode         ← IGameMode, NetworkBehaviour, gestiona el minijuego
        ├── SafetyGameManager      ← singleton, puntuación/tiempo en red
        ├── Cityconstruction       ← escenario visual (COMIENZA DESACTIVADO en runtime)
        │   └── waypoints, sockets, zonas...
        └── WorkerNPC (spawneado dinámicamente en runtime)
```

### Escala del tablero
`SafetyGameEnvironment.localScale = 0.0085`. **Todo objeto que sea hijo hereda esta escala.** Los prefabs de red (conos, barreras, WorkerNPC) deben tener `localScale = Vector3.one` al spawnearse como hijos de `SafetyGameEnvironment`.

### Regla crítica de NGO
Los `NetworkObject` dentro de un GameObject **desactivado** al momento del spawn de NGO son excluidos del spawn y nunca se sincronizan (`IsSpawned = false`). `Cityconstruction` comienza desactivado → cualquier objeto de red que deba replicarse **no** puede ser in-scene dentro de él; debe spawnarse dinámicamente.

## Modo de juego de seguridad (Game Mode ID 5)

### Scripts principales
| Script | Ruta | Responsabilidad |
|--------|------|-----------------|
| `SafetyGameMode` | `Games/SafetyGame/Scripts/` | IGameMode — inicia/termina el juego, spawna WorkerNPC y gestiona dispensers |
| `SafetyGameManager` | `Games/SafetyGame/Scripts/` | Singleton NetworkBehaviour — puntuación, tiempo, registro de NPCs y zonas |
| `SafetyNPC` | `Games/SafetyGame/Scripts/` | Trabajador NPC — movimiento por waypoints, alerta on-hover, UI de alerta replicada |
| `SafetyObjectSocket` | `Games/SafetyGame/Scripts/` | NetworkBehaviour sobre XRSocketInteractor — filtra objetos por tag, notifica HazardZone |
| `SafetyHazardZone` | `Games/SafetyGame/Scripts/` | Zona de peligro con trigger — penaliza si el NPC entra sin suficientes objetos colocados |

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

### Sistema de conos y barreras
- **Prefab de cono**: `SafetyCone Variant` (en `Assets/Samples/`) — variante de `SM_Prop_Cone_01`, tag `SafetyCone`
- **Prefab de barrera**: `SafetyBarrier Variant` (pendiente) — variante de `SM_Prop_Roadblock_02`, tag `SafetyBarrier`
- Ambos necesitan: `Rigidbody` + `BoxCollider` + `XRGrabInteractable` + `XRGeneralGrabTransformer` + `NetworkObject` + `ClientNetworkTransform` + `NetworkPhysicsInteractable`
- Se registran en `_SafetyGamePrefabList` (en `NetworkedPrefabs/`) y se spawnan via `NetworkObjectDispenser`
- Los sockets de colocación usan `XRSocketInteractor` + `SafetyObjectSocket` (filtra por tag)

### Dispensers
- `coneDispenser` — spawna conos (`SafetyCone Variant`)
- `barrierDispenser` — spawna barreras (`SafetyBarrier Variant`)
- Ambos referenciados en `SafetyGameMode` y llamados en `ShowGameMode`/`HideGameMode`
- El prefab a spawnar se asigna en el **child** `InteractableSpawner` → campo `Spawn Interactable Prefab` (tipo `NetworkBaseInteractable`)
- `InteractableSpawner.SpawnInteractablePrefab()` sobreescribe el `localScale` del objeto spawneado con el `lossyScale` del spawn transform → la escala del tablero se aplica automáticamente

## Sistema de game modes
`GameModeManager` ordena por `gameModeID` los hijos que implementen `IGameMode` y llama `ShowGameMode`/`HideGameMode`. El Safety Game tiene ID 5.

`IGameMode` requiere: `gameModeID`, `ShowGameMode()`, `HideGameMode()`.

## Interacción XR (NearFarInteractor)
- El interactor usa `SphereInteractionCaster` (near) con radio ~3.5cm
- Solo detecta objetos en **layer Default** con **physics non-triggers**
- El hover del NPC se dispara via `XRSimpleInteractable.firstHoverEntered` → `SafetyNPC.AlertNPC()`
- Hay cooldown de `m_AlertCooldown` (3s por defecto) para evitar spam de sonido/alerta

## Prefabs de red registrados
- `_SafetyGamePrefabList` — WorkerNPC, (barreras pendiente)
- `_ConeGamePrefabList` — conos

## Paths clave
```
Assets/MRTabletopAssets/Games/SafetyGame/Scripts/   ← scripts del minijuego
Assets/MRTabletopAssets/Scripts/GameModes/           ← GameModeManager, IGameMode
Assets/MRTabletopAssets/Scripts/ObjectDispenser/     ← NetworkObjectDispenser, InteractableSpawner
Assets/MRTabletopAssets/Prefabs/GameModes/           ← SafetyGameEnvironment.prefab
Assets/MRTabletopAssets/Prefabs/GameModes/CityconstructionGame/NetworkedPrefabs/  ← prefab lists
Assets/Samples/                                      ← SafetyCone Variant.prefab
Assets/PolygonConstruction/Prefabs/Props/            ← meshes base (SM_Prop_*)
Assets/MRTabletopAssets/Scripts/Debug/               ← NearCasterGizmo
```

## Convenciones del proyecto
- Campos serializados: prefijo `m_` (ej. `m_WorkerNpcPrefab`)
- NetworkVariables: siempre con `ReadPermission.Everyone`, `WritePermission.Server` salvo excepción
- Sin `NetworkTransform` para el NPC — se usa sincronización manual via NetworkVariables (evita conflictos con la escala del tablero)
- Gizmos de waypoints: `OnDrawGizmosSelected` en `SafetyGameMode` (se ven al seleccionar `SafetyGameEnvironment`)
