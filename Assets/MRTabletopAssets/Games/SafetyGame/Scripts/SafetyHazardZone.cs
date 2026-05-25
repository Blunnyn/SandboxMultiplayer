using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Representa una zona peligrosa en la construcción (ej. excavación abierta).
    /// Detecta NPCs y aplica penalizaciones si no se encuentra delimitada con conos de seguridad.
    /// </summary>
    public class SafetyHazardZone : NetworkBehaviour
    {
        [Header("Configuración de Seguridad")]
        [SerializeField] private int m_RequiredCones = 4;
        [SerializeField] private int m_PenaltyAmount = 15;
        [SerializeField] private float m_PenaltyIntervalSeconds = 1.0f;

        // Variables de red sincronizadas
        public NetworkVariable<bool> isDelimited = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> activeConesCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private HashSet<SafetyNPC> m_NPCsInZone = new HashSet<SafetyNPC>();
        private float m_PenaltyTimer = 0.0f;

        private void Start()
        {
            if (SafetyGameManager.Instance != null)
            {
                SafetyGameManager.Instance.RegisterHazardZone(this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (IsServer)
            {
                isDelimited.Value = false;
                activeConesCount.Value = 0;
            }
        }

        private void Update()
        {
            // Solo el servidor gestiona las penalizaciones
            if (!IsServer || isDelimited.Value || m_NPCsInZone.Count == 0) return;

            // Verificar si el juego sigue activo
            if (SafetyGameManager.Instance == null || !SafetyGameManager.Instance.isGameActive.Value) return;

            m_PenaltyTimer += Time.deltaTime;
            if (m_PenaltyTimer >= m_PenaltyIntervalSeconds)
            {
                m_PenaltyTimer = 0.0f;
                
                // Penalización acumulada por cada NPC que esté dentro del peligro
                int totalPenalty = m_PenaltyAmount * m_NPCsInZone.Count;
                SafetyGameManager.Instance.ApplyPenalty(totalPenalty);
                
                Debug.LogWarning($"[Safety Hazard Zone] NPC en peligro. Penalización de {totalPenalty} puntos aplicada.");
            }
        }

        /// <summary>
        /// Actualiza el conteo de conos y comprueba si la zona está delimitada.
        /// </summary>
        public void UpdateConesCount(int count)
        {
            if (!IsServer) return;

            activeConesCount.Value = count;
            bool wasDelimited = isDelimited.Value;
            isDelimited.Value = activeConesCount.Value >= m_RequiredCones;

            // Si la zona pasa a estar segura, liberamos a los NPCs en peligro dentro de ella
            if (isDelimited.Value && !wasDelimited)
            {
                foreach (var npc in m_NPCsInZone)
                {
                    if (npc != null)
                    {
                        npc.SetInDanger(false);
                    }
                }
                Debug.Log($"[Safety Hazard Zone] La zona ha sido delimitada correctamente con {activeConesCount.Value} conos.");
            }
            // Si deja de estar segura (se removió un cono), marcar peligro de nuevo
            else if (!isDelimited.Value && wasDelimited)
            {
                foreach (var npc in m_NPCsInZone)
                {
                    if (npc != null)
                    {
                        npc.SetInDanger(true);
                    }
                }
                Debug.LogWarning("[Safety Hazard Zone] Zona de riesgo comprometida. Faltan conos de delimitación.");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;

            SafetyNPC npc = other.GetComponentInParent<SafetyNPC>();
            if (npc != null)
            {
                m_NPCsInZone.Add(npc);
                
                // Si la zona no está delimitada, el NPC entra en estado de peligro
                if (!isDelimited.Value)
                {
                    npc.SetInDanger(true);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;

            SafetyNPC npc = other.GetComponentInParent<SafetyNPC>();
            if (npc != null)
            {
                m_NPCsInZone.Remove(npc);
                npc.SetInDanger(false);
            }
        }
    }
}
