using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Detecta la colocación de objetos de seguridad en un XRSocketInteractor (filtrados por tag)
    /// e informa opcionalmente a una SafetyHazardZone.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class SafetyObjectSocket : NetworkBehaviour
    {
        [Header("Configuración")]
        [Tooltip("Tag que debe tener el objeto para ser aceptado (ej. SafetyCone, SafetyBarrier).")]
        [SerializeField] private string m_ObjectTag = "SafetyCone";

        [Header("Zona de Peligro (opcional)")]
        [Tooltip("Si se asigna, notifica a la zona cuando un objeto entra o sale. Dejar vacío si no se necesita lógica de penalización.")]
        [SerializeField] private SafetyHazardZone m_HazardZone;

        private XRSocketInteractor m_SocketInteractor;

        /// <summary>Tag que este socket acepta (ej. SafetyCone, SafetyBarrier).</summary>
        public string ObjectTag => m_ObjectTag;

        private void Awake()
        {
            m_SocketInteractor = GetComponent<XRSocketInteractor>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (SafetyGameManager.Instance != null)
                SafetyGameManager.Instance.RegisterObjectSocket(this);
        }

        /// <summary>
        /// Indica si el socket tiene colocado un objeto válido (del tag esperado).
        /// </summary>
        public bool IsOccupiedByValidObject()
        {
            if (m_SocketInteractor == null || !m_SocketInteractor.hasSelection) return false;

            var selected = m_SocketInteractor.firstInteractableSelected;
            return selected != null && selected.transform.CompareTag(m_ObjectTag);
        }

        private void OnEnable()
        {
            if (m_SocketInteractor == null) return;
            m_SocketInteractor.selectEntered.AddListener(OnObjectPlaced);
            m_SocketInteractor.selectExited.AddListener(OnObjectRemoved);
        }

        private void OnDisable()
        {
            if (m_SocketInteractor == null) return;
            m_SocketInteractor.selectEntered.RemoveListener(OnObjectPlaced);
            m_SocketInteractor.selectExited.RemoveListener(OnObjectRemoved);
        }

        private void OnObjectPlaced(SelectEnterEventArgs args)
        {
            if (args.interactableObject.transform.CompareTag(m_ObjectTag))
            {
                NotifyHazardZone();
                NotifyObjectivesManager();
            }
        }

        private void OnObjectRemoved(SelectExitEventArgs args)
        {
            if (args.interactableObject.transform.CompareTag(m_ObjectTag))
            {
                NotifyHazardZone();
                NotifyObjectivesManager();
            }
        }

        /// <summary>
        /// Pide al manager que recuente los objetivos colocados (server-authoritative).
        /// </summary>
        private void NotifyObjectivesManager()
        {
            if (SafetyGameManager.Instance == null) return;

            if (SafetyGameManager.Instance.IsServer)
                SafetyGameManager.Instance.RecountPlacedObjects();
            else
                RecountObjectivesServerRpc();
        }

        [Rpc(SendTo.Server)]
        private void RecountObjectivesServerRpc()
        {
            if (SafetyGameManager.Instance != null)
                SafetyGameManager.Instance.RecountPlacedObjects();
        }

        private void NotifyHazardZone()
        {
            if (m_HazardZone == null) return;

            SafetyObjectSocket[] socketsInZone = m_HazardZone.GetComponentsInChildren<SafetyObjectSocket>();
            int occupiedCount = 0;

            foreach (var socket in socketsInZone)
            {
                if (socket == null || socket.m_SocketInteractor == null || !socket.m_SocketInteractor.hasSelection) continue;
                var selected = socket.m_SocketInteractor.firstInteractableSelected;
                if (selected != null && selected.transform.CompareTag(m_ObjectTag))
                    occupiedCount++;
            }

            if (SafetyGameManager.Instance != null)
            {
                if (SafetyGameManager.Instance.IsServer)
                    m_HazardZone.UpdateConesCount(occupiedCount);
                else
                    UpdateCountServerRpc(occupiedCount);
            }
        }

        [Rpc(SendTo.Server)]
        private void UpdateCountServerRpc(int count)
        {
            if (m_HazardZone != null)
                m_HazardZone.UpdateConesCount(count);
        }
    }
}
