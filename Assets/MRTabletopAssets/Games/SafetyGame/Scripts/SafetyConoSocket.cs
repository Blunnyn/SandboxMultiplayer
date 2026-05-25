using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Componente que se asocia a un XRSocketInteractor.
    /// Detecta la colocación de conos de seguridad (filtrados por tag) e informa a la SafetyHazardZone.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class SafetyConoSocket : NetworkBehaviour
    {
        [Header("Referencias de Zona")]
        [SerializeField] private SafetyHazardZone m_HazardZone;
        [SerializeField] private string m_ConeTag = "SafetyCone";

        private XRSocketInteractor m_SocketInteractor;

        private void Awake()
        {
            m_SocketInteractor = GetComponent<XRSocketInteractor>();
        }

        private void OnEnable()
        {
            if (m_SocketInteractor != null)
            {
                m_SocketInteractor.selectEntered.AddListener(OnConePlaced);
                m_SocketInteractor.selectExited.AddListener(OnConeRemoved);
            }
        }

        private void OnDisable()
        {
            if (m_SocketInteractor != null)
            {
                m_SocketInteractor.selectEntered.RemoveListener(OnConePlaced);
                m_SocketInteractor.selectExited.RemoveListener(OnConeRemoved);
            }
        }

        private void OnConePlaced(SelectEnterEventArgs args)
        {
            if (args.interactableObject.transform.CompareTag(m_ConeTag))
            {
                NotifyHazardZone();
            }
        }

        private void OnConeRemoved(SelectExitEventArgs args)
        {
            if (args.interactableObject.transform.CompareTag(m_ConeTag))
            {
                NotifyHazardZone();
            }
        }

        private void NotifyHazardZone()
        {
            if (m_HazardZone == null) return;

            // Encontrar todos los SafetyConoSocket hijos o asociados a la misma zona de peligro
            SafetyConoSocket[] socketsInZone = m_HazardZone.GetComponentsInChildren<SafetyConoSocket>();
            int totalOccupiedCones = 0;

            foreach (var socket in socketsInZone)
            {
                if (socket != null && socket.m_SocketInteractor != null && socket.m_SocketInteractor.hasSelection)
                {
                    var selectedObject = socket.m_SocketInteractor.firstInteractableSelected;
                    if (selectedObject != null && selectedObject.transform.CompareTag(m_ConeTag))
                    {
                        totalOccupiedCones++;
                    }
                }
            }

            // Reportar al servidor el conteo de conos
            if (SafetyGameManager.Instance != null)
            {
                if (SafetyGameManager.Instance.IsServer)
                {
                    m_HazardZone.UpdateConesCount(totalOccupiedCones);
                }
                else
                {
                    UpdateConesCountServerRpc(totalOccupiedCones);
                }
            }
        }

        [Rpc(SendTo.Server)]
        private void UpdateConesCountServerRpc(int count)
        {
            if (m_HazardZone != null)
            {
                m_HazardZone.UpdateConesCount(count);
            }
        }
    }
}
