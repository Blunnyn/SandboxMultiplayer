using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Representa un trabajador NPC en la construcción.
    /// Se desplaza entre waypoints definidos y reacciona cuando los jugadores le alertan de peligros.
    /// </summary>
    public class SafetyNPC : NetworkBehaviour
    {
        [Header("Configuración de Movimiento")]
        [SerializeField] private Transform[] m_Waypoints;
        [SerializeField] private float m_MovementSpeed = 1.0f;
        [SerializeField] private float m_AlertDuration = 3.5f;

        // Variables de red para sincronizar los estados del NPC
        public NetworkVariable<bool> isInDanger = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> isAlerted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private int m_CurrentWaypointIndex = 0;
        private bool m_IsStopped = false;
        private Coroutine m_ResumeMovementCoroutine;

        private void Start()
        {
            if (SafetyGameManager.Instance != null)
            {
                SafetyGameManager.Instance.RegisterNPC(this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (IsServer)
            {
                isInDanger.Value = false;
                isAlerted.Value = false;
            }
        }

        private void Update()
        {
            // Solo el servidor calcula y mueve al NPC (autoridad del servidor)
            if (!IsServer || m_IsStopped || m_Waypoints.Length == 0) return;

            // Si el juego ha terminado, no mover
            if (SafetyGameManager.Instance != null && !SafetyGameManager.Instance.isGameActive.Value) return;

            MoveTowardsWaypoint();
        }

        private void MoveTowardsWaypoint()
        {
            Transform targetWaypoint = m_Waypoints[m_CurrentWaypointIndex];
            if (targetWaypoint == null) return;

            Vector3 targetPosition = targetWaypoint.position;
            // Alinear al plano de la mesa para evitar desplazamientos verticales extraños
            targetPosition.y = transform.position.y;

            Vector3 moveDirection = (targetPosition - transform.position).normalized;
            transform.position += moveDirection * m_MovementSpeed * Time.deltaTime;

            if (moveDirection != Vector3.zero)
            {
                // Rotar suavemente hacia el waypoint
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 6f);
            }

            // Comprobar si llegó al destino
            if (Vector3.Distance(transform.position, targetPosition) < 0.05f)
            {
                m_CurrentWaypointIndex = (m_CurrentWaypointIndex + 1) % m_Waypoints.Length;
            }
        }

        /// <summary>
        /// Alerta al NPC desde el cliente que interactúa con él (mandos o manos).
        /// </summary>
        public void AlertNPC()
        {
            if (isAlerted.Value) return;

            if (IsOwner)
            {
                ApplyAlert();
            }
            else
            {
                AlertNPCServerRpc();
            }
        }

        [Rpc(SendTo.Server)]
        private void AlertNPCServerRpc()
        {
            ApplyAlert();
        }

        private void ApplyAlert()
        {
            if (!IsServer) return;

            isAlerted.Value = true;
            m_IsStopped = true;

            if (m_ResumeMovementCoroutine != null)
            {
                StopCoroutine(m_ResumeMovementCoroutine);
            }
            m_ResumeMovementCoroutine = StartCoroutine(ResumeMovementRoutine());
        }

        private IEnumerator ResumeMovementRoutine()
        {
            yield return new WaitForSeconds(m_AlertDuration);
            
            if (IsServer)
            {
                m_IsStopped = false;
                isAlerted.Value = false;
            }
        }

        /// <summary>
        /// Detiene al NPC permanentemente (ej. al terminar la partida).
        /// </summary>
        public void StopNPC()
        {
            if (!IsServer) return;

            m_IsStopped = true;
            if (m_ResumeMovementCoroutine != null)
            {
                StopCoroutine(m_ResumeMovementCoroutine);
            }
        }

        /// <summary>
        /// Cambia el estado de peligro del NPC.
        /// </summary>
        public void SetInDanger(bool state)
        {
            if (!IsServer) return;

            isInDanger.Value = state;
        }
    }
}
