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
        [SerializeField] private float m_ArrivalThreshold = 0.001f;
        [SerializeField] private float m_AlertDuration = 3.5f;
        [SerializeField] private float m_EndOfRouteWaitTime = 20f;
        [SerializeField] private Animator m_Animator;

        // Variables de red para sincronizar los estados del NPC
        public NetworkVariable<bool> isInDanger = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> isAlerted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Sincronización de posición y rotación (el NetworkTransform no funciona en scene overrides)
        private NetworkVariable<Vector3> m_NetPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<float> m_NetRotationY = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<bool> m_NetIsWalking = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private int m_CurrentWaypointIndex = 0;
        private bool m_IsStopped = false;
        private bool m_HasNetworkData = false;
        private Coroutine m_StartMovementCoroutine;
        private Coroutine m_ResumeMovementCoroutine;
        private Coroutine m_EndOfRouteCoroutine;

        private void Start()
        {
            if (SafetyGameManager.Instance != null)
                SafetyGameManager.Instance.RegisterNPC(this);

            m_IsStopped = true;
            if (m_Animator != null) m_Animator.SetBool("IsWalking", false);
        }

        private void OnEnable()
        {
            if (m_Animator != null) m_Animator.SetBool("IsWalking", false);

            if (m_Waypoints.Length == 0) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            if (m_StartMovementCoroutine != null) StopCoroutine(m_StartMovementCoroutine);
            m_StartMovementCoroutine = StartCoroutine(StartMovementWhenReady());
        }

        private void OnDisable()
        {
            if (m_StartMovementCoroutine != null)
            {
                StopCoroutine(m_StartMovementCoroutine);
                m_StartMovementCoroutine = null;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (IsServer && SafetyGameManager.Instance != null)
                SafetyGameManager.Instance.isGameActive.OnValueChanged -= OnGameActiveChanged;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                isInDanger.Value = false;
                isAlerted.Value = false;
                m_NetPosition.Value = transform.position;
                m_NetRotationY.Value = transform.eulerAngles.y;
                m_NetIsWalking.Value = false;
                // El inicio del movimiento se maneja en OnEnable, que corre después del spawn
            }
            else
            {
                // Clientes: NO sobrescribir la posición de escena con un valor potencialmente
                // sin inicializar (Vector3.zero). Esperar a recibir una posición real del servidor.
                m_NetPosition.OnValueChanged += (_, _) => m_HasNetworkData = true;
                if (m_NetPosition.Value != Vector3.zero)
                {
                    m_HasNetworkData = true;
                    transform.position = m_NetPosition.Value;
                }

                m_NetIsWalking.OnValueChanged += (_, current) =>
                {
                    if (m_Animator != null) m_Animator.SetBool("IsWalking", current);
                };
                if (m_Animator != null) m_Animator.SetBool("IsWalking", m_NetIsWalking.Value);
            }
        }

        private IEnumerator StartMovementWhenReady()
        {
            yield return new WaitUntil(() =>
                SafetyGameManager.Instance != null &&
                SafetyGameManager.Instance.isGameActive.Value);

            if (m_Waypoints.Length == 0) yield break;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) yield break;

            SafetyGameManager.Instance.isGameActive.OnValueChanged += OnGameActiveChanged;
            StartNPC();
        }

        private void OnGameActiveChanged(bool previous, bool current)
        {
            if (!IsServer) return;
            if (current)
                StartNPC();
            else
                StopNPC();
        }

        public void StartNPC()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || m_Waypoints.Length == 0) return;
            m_IsStopped = false;
            m_NetIsWalking.Value = true;
            if (m_Animator != null) m_Animator.SetBool("IsWalking", true);
        }

        private void Update()
        {
            if (NetworkManager.Singleton == null) return;

            if (NetworkManager.Singleton.IsServer)
            {
                if (!m_IsStopped && m_Waypoints.Length > 0)
                    MoveTowardsWaypoint();

                // Solo escribir NetworkVariables si el NB está correctamente spawneado
                if (IsSpawned)
                {
                    m_NetPosition.Value = transform.position;
                    m_NetRotationY.Value = transform.eulerAngles.y;
                }
            }
            else
            {
                if (!m_HasNetworkData) return;
                transform.position = Vector3.Lerp(transform.position, m_NetPosition.Value, Time.deltaTime * 15f);
                Vector3 e = transform.eulerAngles;
                e.y = Mathf.LerpAngle(e.y, m_NetRotationY.Value, Time.deltaTime * 15f);
                transform.eulerAngles = e;
            }
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
            if (Vector3.Distance(transform.position, targetPosition) < m_ArrivalThreshold)
            {
                if (m_CurrentWaypointIndex == m_Waypoints.Length - 1)
                {
                    m_CurrentWaypointIndex = 0;
                    m_IsStopped = true;
                    if (m_EndOfRouteCoroutine != null) StopCoroutine(m_EndOfRouteCoroutine);
                    m_EndOfRouteCoroutine = StartCoroutine(WaitAtEndOfRoute());
                }
                else
                {
                    m_CurrentWaypointIndex++;
                }
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
            m_NetIsWalking.Value = false;
            if (m_Animator != null) m_Animator.SetBool("IsWalking", false);

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
                m_NetIsWalking.Value = true;
                if (m_Animator != null) m_Animator.SetBool("IsWalking", true);
            }
        }

        private IEnumerator WaitAtEndOfRoute()
        {
            m_NetIsWalking.Value = false;
            if (m_Animator != null) m_Animator.SetBool("IsWalking", false);
            yield return new WaitForSeconds(m_EndOfRouteWaitTime);
            if (IsServer)
            {
                m_IsStopped = false;
                m_NetIsWalking.Value = true;
                if (m_Animator != null) m_Animator.SetBool("IsWalking", true);
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

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (m_Waypoints == null || m_Waypoints.Length == 0) return;

            float scale = transform.lossyScale.x;
            float sphereRadius = scale * 1.2f;

            for (int i = 0; i < m_Waypoints.Length; i++)
            {
                if (m_Waypoints[i] == null) continue;

                Vector3 pos = m_Waypoints[i].position;

                // Esfera amarilla en cada waypoint
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(pos, sphereRadius);

                // Número de orden con label
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(pos + Vector3.up * sphereRadius * 2f, $"{i + 1}");

                // Línea hacia el siguiente waypoint
                int next = (i + 1) % m_Waypoints.Length;
                if (m_Waypoints[next] != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(pos, m_Waypoints[next].position);
                }
            }

            // Círculo de llegada alrededor de cada waypoint (muestra el umbral exacto)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            foreach (var wp in m_Waypoints)
            {
                if (wp != null)
                    Gizmos.DrawWireSphere(wp.position, m_ArrivalThreshold);
            }
        }
#endif
    }
}

