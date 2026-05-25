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

        // Variables de red sincronizadas para todos los clientes
        public NetworkVariable<int> score = new NetworkVariable<int>(1000, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<float> timeRemaining = new NetworkVariable<float>(180f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> isGameActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private List<SafetyHazardZone> m_RegisteredHazardZones = new List<SafetyHazardZone>();
        private List<SafetyNPC> m_RegisteredNPCs = new List<SafetyNPC>();

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
            }
        }

        private void Update()
        {
            if (!IsServer || !isGameActive.Value) return;

            // Actualizar tiempo de ronda en el servidor
            timeRemaining.Value -= Time.deltaTime;
            if (timeRemaining.Value <= 0)
            {
                timeRemaining.Value = 0f;
                EndGame();
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
