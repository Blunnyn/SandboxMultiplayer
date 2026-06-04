using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Gestiona el estado global del minijuego de seguridad y riesgos en construcción civil.
    /// Sincroniza la puntuación, el tiempo restante y el estado de la partida en red.
    /// </summary>
    public class SafetyGameManager : NetworkBehaviour
    {
        public static SafetyGameManager Instance { get; private set; }

        [Header("Configuración del Juego")]
        [SerializeField] private float m_RoundDuration = 180f;
        [SerializeField] private int m_StartingScore = 1000;

        [Header("Objetivos (conos y barreras a colocar)")]
        [Tooltip("Total de conos que el escenario requiere colocar para completar el objetivo.")]
        [SerializeField] private int m_ConesRequired = 8;
        [Tooltip("Total de barreras que el escenario requiere colocar para completar el objetivo.")]
        [SerializeField] private int m_BarriersRequired = 2;

        // Tags que distinguen el tipo de objeto colocado en cada socket.
        private const string k_ConeTag = "SafetyCone";
        private const string k_BarrierTag = "SafetyBarrier";

        // Variables de red sincronizadas para todos los clientes
        public NetworkVariable<int> score = new NetworkVariable<int>(1000, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<float> timeRemaining = new NetworkVariable<float>(180f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> isGameActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Progreso de objetivos (colocados vs requeridos). El UI los lee para mostrar cuántos faltan.
        public NetworkVariable<int> conesPlaced = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> conesRequired = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> barriersPlaced = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> barriersRequired = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Latch de objetivos completados: el servidor lo pone a true cuando se cumplen todos los
        // requisitos y NO lo baja aunque luego se quite un objeto (evita que la pantalla final parpadee).
        // Se restablece a false solo al reiniciar el escenario. El UI de fin de partida lo lee.
        public NetworkVariable<bool> objectivesCompleted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private List<SafetyHazardZone> m_RegisteredHazardZones = new List<SafetyHazardZone>();
        private List<SafetyNPC> m_RegisteredNPCs = new List<SafetyNPC>();
        private List<SafetyObjectSocket> m_RegisteredSockets = new List<SafetyObjectSocket>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (IsServer)
            {
                score.Value = m_StartingScore;
                timeRemaining.Value = m_RoundDuration;
                isGameActive.Value = true;

                conesRequired.Value = m_ConesRequired;
                barriersRequired.Value = m_BarriersRequired;
                RecountPlacedObjects();
            }
        }

        /// <summary>
        /// Aplica una penalización de puntos en el servidor.
        /// </summary>
        public void ApplyPenalty(int penaltyPoints)
        {
            if (!IsServer || !isGameActive.Value) return;

            score.Value = Mathf.Max(0, score.Value - penaltyPoints);
        }

        /// <summary>
        /// Agrega puntos al marcador general en el servidor.
        /// </summary>
        public void AddPoints(int points)
        {
            if (!IsServer || !isGameActive.Value) return;

            score.Value += points;
        }

        /// <summary>
        /// Registra una zona de peligro activa.
        /// </summary>
        public void RegisterHazardZone(SafetyHazardZone zone)
        {
            if (!m_RegisteredHazardZones.Contains(zone))
            {
                m_RegisteredHazardZones.Add(zone);
            }
        }

        /// <summary>
        /// Registra un NPC en la simulación.
        /// </summary>
        public void RegisterNPC(SafetyNPC npc)
        {
            if (!m_RegisteredNPCs.Contains(npc))
            {
                m_RegisteredNPCs.Add(npc);
            }
        }

        /// <summary>
        /// Registra un socket de colocación de objetos para el conteo de objetivos.
        /// </summary>
        public void RegisterObjectSocket(SafetyObjectSocket socket)
        {
            if (socket != null && !m_RegisteredSockets.Contains(socket))
            {
                m_RegisteredSockets.Add(socket);
                RecountPlacedObjects();
            }
        }

        /// <summary>
        /// Recuenta cuántos conos y barreras hay colocados entre todos los sockets registrados.
        /// Solo el servidor escribe las NetworkVariables; los clientes las leen para el UI.
        /// </summary>
        public void RecountPlacedObjects()
        {
            if (!IsServer) return;

            int cones = 0;
            int barriers = 0;

            foreach (var socket in m_RegisteredSockets)
            {
                if (socket == null || !socket.IsOccupiedByValidObject()) continue;

                if (socket.ObjectTag == k_ConeTag) cones++;
                else if (socket.ObjectTag == k_BarrierTag) barriers++;
            }

            conesPlaced.Value = cones;
            barriersPlaced.Value = barriers;

            // Latch de victoria: una vez completados, no se baja aunque luego quiten objetos.
            // Solo RestartScenario() lo restablece a false.
            if (!objectivesCompleted.Value &&
                cones >= conesRequired.Value && barriers >= barriersRequired.Value)
            {
                objectivesCompleted.Value = true;
            }
        }

        /// <summary>
        /// Reinicia el escenario: despawnea todos los conos y barreras (colocados y sueltos),
        /// vuelve el conteo a 0 y baja el latch de objetivos completados. Solo servidor.
        /// </summary>
        public void RestartScenario()
        {
            if (!IsServer) return;

            DespawnAllByTag(k_ConeTag);
            DespawnAllByTag(k_BarrierTag);

            RecountPlacedObjects();
            objectivesCompleted.Value = false;

            Debug.Log("[Safety Game Manager] Escenario reiniciado: conos y barreras despawneados, conteo a 0.");
        }

        /// <summary>
        /// Despawnea todos los NetworkObjects de la escena que tengan el tag indicado.
        /// </summary>
        private void DespawnAllByTag(string tag)
        {
            GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);
            foreach (var go in objects)
            {
                if (go.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
            }
        }

        /// <summary>
        /// Punto de entrada para el botón de reinicio (UI local). Ejecuta en servidor;
        /// si lo llama un cliente, lo reenvía vía RPC.
        /// </summary>
        public void RequestRestart()
        {
            if (IsServer)
                RestartScenario();
            else
                RequestRestartServerRpc();
        }

        [Rpc(SendTo.Server)]
        private void RequestRestartServerRpc()
        {
            RestartScenario();
        }

        private void EndGame()
        {
            if (!IsServer) return;

            isGameActive.Value = false;
            
            // Detener el comportamiento de todos los NPCs registrados
            foreach (var npc in m_RegisteredNPCs)
            {
                if (npc != null)
                {
                    npc.StopNPC();
                }
            }

            Debug.Log($"[Safety Game Manager] Juego finalizado. Puntuación final: {score.Value}");
        }
    }
}
