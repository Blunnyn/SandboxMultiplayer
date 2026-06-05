using System;
using Unity.Netcode;
using UnityEngine;
using XRMultiplayer.SafetyGame;

namespace UnityEngine.XR.Templates.MRTTabletopAssets
{
    /// <summary>
    /// Modo de juego para el minijuego de seguridad y riesgos en construcción civil.
    /// Implementa IGameMode para integrarse con el GameModeManager del tablero.
    /// </summary>
    public class SafetyGameMode : NetworkBehaviour, IGameMode
    {
        /// <summary>
        /// Se invoca en todos los clientes al mostrar (true) u ocultar (false) este modo de juego.
        /// Permite que UIs locales reaccionen sin acoplamiento directo de jerarquía.
        /// </summary>
        public static event Action<bool> OnSafetyGameToggled;
        [Header("Configuración del Modo de Juego")]
        [SerializeField] private int m_GameModeID = 5;

        [Tooltip("El contenedor principal de los elementos del minijuego (terreno, NPCs, zonas) que se activará o desactivará.")]
        public GameObject gameModeContainer;

        [Tooltip("Dispensador de conos del minijuego. Necesario para reiniciar sus corrutinas al mostrar el modo.")]
        public NetworkObjectDispenser coneDispenser;

        [Tooltip("Dispensador de barreras del minijuego.")]
        public NetworkObjectDispenser barrierDispenser;

        [Header("UI Local (se activa/desactiva en todos los clientes)")]
        [Tooltip("Panel de introducción/objetivos. No es un NetworkObject: ShowGameMode lo activa localmente en cada jugador.")]
        [SerializeField] private GameObject m_IntroUI;

        [Header("Trabajador NPC (spawn dinámico)")]
        [Tooltip("Prefab de red del WorkerNPC. Debe estar registrado en la NetworkPrefabsList del NetworkManager.")]
        [SerializeField] private NetworkObject m_WorkerNpcPrefab;

        [Tooltip("Ruta de waypoints que recorrerá el trabajador. Objetos de escena dentro del Cityconstruction.")]
        [SerializeField] private Transform[] m_WorkerWaypoints;

        // Instancia spawneada del trabajador (solo válida en el servidor).
        private NetworkObject m_SpawnedWorker;

        /// <summary>
        /// ID del modo de juego para el GameModeManager.
        /// </summary>
        public int gameModeID => m_GameModeID;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Garantizar que iniciamos en el estado correcto (oculto)
            if (gameModeContainer != null)
            {
                gameModeContainer.SetActive(false);
            }
        }

        /// <summary>
        /// Oculta los elementos visuales del escenario al cambiar de juego.
        /// </summary>
        public void HideGameMode()
        {
            Debug.Log($"[Safety Game Mode] Ocultando el modo de juego (Servidor: {IsServer}, Cliente ID: {NetworkManager.Singleton.LocalClientId})");

            OnSafetyGameToggled?.Invoke(false);

            if (m_IntroUI != null) m_IntroUI.SetActive(false);

            if (coneDispenser != null && coneDispenser.gameObject.activeInHierarchy)
            {
                coneDispenser.Hide();
            }

            if (barrierDispenser != null && barrierDispenser.gameObject.activeInHierarchy)
            {
                barrierDispenser.Hide();
            }

            if (gameModeContainer != null)
            {
                gameModeContainer.SetActive(false);
            }

            OnGameModeEnd();
        }

        /// <summary>
        /// Muestra el escenario cuando el juego es seleccionado en la mesa virtual.
        /// </summary>
        public void ShowGameMode()
        {
            Debug.Log($"[Safety Game Mode] Mostrando el modo de juego (Servidor: {IsServer}, Cliente ID: {NetworkManager.Singleton.LocalClientId})");

            OnSafetyGameToggled?.Invoke(true);

            if (m_IntroUI != null) m_IntroUI.SetActive(true);

            if (gameModeContainer != null)
            {
                gameModeContainer.SetActive(true);
            }

            if (coneDispenser != null)
            {
                coneDispenser.Show();
            }

            if (barrierDispenser != null)
            {
                barrierDispenser.Show();
            }

            OnGameModeStart();
        }

        /// <summary>
        /// Inicializa el estado del juego cuando comienza formalmente la partida.
        /// </summary>
        public void OnGameModeStart()
        {
            Debug.Log($"[Safety Game Mode] OnGameModeStart invocado. (Servidor: {IsServer})");
            // Solo el servidor inicializa y activa el juego en red
            if (!IsServer) return;

            // Spawn dinámico del trabajador. Se hace primero para que el NPC arranque
            // aunque el SafetyGameManager (sobre el contenedor) aún no esté sincronizado.
            SpawnWorker();

            if (SafetyGameManager.Instance != null)
            {
                SafetyGameManager.Instance.isGameActive.Value = true;
                // Restablecer valores de la partida
                SafetyGameManager.Instance.timeRemaining.Value = 180f; // Duración por defecto
                SafetyGameManager.Instance.score.Value = 1000;         // Puntos iniciales
                Debug.Log("[Safety Game Mode] Minijuego de seguridad iniciado en el servidor.");
            }
            else
            {
                Debug.LogWarning("[Safety Game Mode] No se pudo iniciar el juego: SafetyGameManager.Instance es nulo.");
            }
        }

        /// <summary>
        /// Detiene la simulación del minijuego cuando termina o se cambia de juego.
        /// </summary>
        public void OnGameModeEnd()
        {
            Debug.Log($"[Safety Game Mode] OnGameModeEnd invocado. (Servidor: {IsServer})");
            if (!IsServer) return;

            DespawnWorker();

            if (SafetyGameManager.Instance != null)
            {
                SafetyGameManager.Instance.isGameActive.Value = false;
                Debug.Log("[Safety Game Mode] Minijuego de seguridad finalizado en el servidor.");
            }
        }

        /// <summary>
        /// Instancia y spawnea el trabajador como hijo de SafetyGameEnvironment (este objeto),
        /// heredando su escala de mesa (0.0085). Solo servidor.
        /// </summary>
        private void SpawnWorker()
        {
            if (!IsServer || m_SpawnedWorker != null) return;

            if (m_WorkerNpcPrefab == null)
            {
                Debug.LogWarning("[Safety Game Mode] No hay prefab de WorkerNPC asignado; no se puede spawnear el trabajador.");
                return;
            }

            // Instanciar como hijo de este objeto (escala 0.0085) sin pasar a espacio de mundo,
            // para que conserve localScale = 1 y herede la escala del padre.
            NetworkObject worker = Instantiate(m_WorkerNpcPrefab, transform);
            worker.transform.localScale = Vector3.one;

            // Colocar en el primer waypoint (posición de mundo) si existe la ruta.
            if (m_WorkerWaypoints != null && m_WorkerWaypoints.Length > 0 && m_WorkerWaypoints[0] != null)
            {
                worker.transform.position = m_WorkerWaypoints[0].position;
            }

            worker.Spawn(true);

            SafetyNPC npc = worker.GetComponent<SafetyNPC>();
            if (npc != null)
            {
                npc.InitializeRoute(m_WorkerWaypoints);
            }

            m_SpawnedWorker = worker;
            Debug.Log("[Safety Game Mode] WorkerNPC spawneado y ruta asignada.");
        }

        /// <summary>
        /// Despawnea y destruye al trabajador. Solo servidor.
        /// </summary>
        private void DespawnWorker()
        {
            if (m_SpawnedWorker == null) return;

            if (m_SpawnedWorker.IsSpawned)
            {
                m_SpawnedWorker.Despawn(true);
            }
            else
            {
                Destroy(m_SpawnedWorker.gameObject);
            }

            m_SpawnedWorker = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (m_WorkerWaypoints == null || m_WorkerWaypoints.Length == 0) return;

            float scale = transform.lossyScale.x;
            float sphereRadius = scale * 1.2f;

            for (int i = 0; i < m_WorkerWaypoints.Length; i++)
            {
                if (m_WorkerWaypoints[i] == null) continue;

                Vector3 pos = m_WorkerWaypoints[i].position;

                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(pos, sphereRadius);

                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(pos + Vector3.up * sphereRadius * 2f, $"{i + 1}");

                int next = (i + 1) % m_WorkerWaypoints.Length;
                if (m_WorkerWaypoints[next] != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(pos, m_WorkerWaypoints[next].position);
                }
            }

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            foreach (var wp in m_WorkerWaypoints)
            {
                if (wp != null)
                    Gizmos.DrawWireSphere(wp.position, 0.001f);
            }
        }
#endif
    }
}
