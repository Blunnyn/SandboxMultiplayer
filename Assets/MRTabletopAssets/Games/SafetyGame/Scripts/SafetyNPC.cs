using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Representa un trabajador NPC en la construcción.
    /// Se instancia dinámicamente (spawn de red) cuando arranca el minijuego y recorre
    /// la ruta de waypoints que le entrega el SafetyGameMode. Reacciona cuando los
    /// jugadores le alertan de peligros.
    /// </summary>
    public class SafetyNPC : NetworkBehaviour
    {
        [Header("Configuración de Movimiento")]
        [SerializeField] private float m_MovementSpeed = 0.02f;
        [SerializeField] private float m_ArrivalThreshold = 0.001f;
        [SerializeField] private float m_AlertDuration = 3.5f;
        [SerializeField] private float m_AlertCooldown = 3f;
        [SerializeField] private float m_EndOfRouteWaitTime = 20f;
        [SerializeField] private Animator m_Animator;

        [Header("UI de Alerta")]
        [Tooltip("GameObject hijo dentro del Canvas que se activa/desactiva para todos los jugadores cuando se interactúa con el trabajador (p.ej. el panel o imagen de alerta, no el Canvas raíz).")]
        [SerializeField] private GameObject m_AlertUIContent;
        [Tooltip("AudioSource que reproduce el sonido de alerta. Se dispara una sola vez por ciclo, desde código, no desde eventos de hover.")]
        [SerializeField] private AudioSource m_AlertAudioSource;

        // La ruta se asigna en runtime desde el servidor (los waypoints son objetos de escena,
        // no se pueden serializar en el prefab). Solo el servidor la utiliza.
        private Transform[] m_Waypoints;

        // Variables de red para sincronizar los estados del NPC
        public NetworkVariable<bool> isInDanger = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> isAlerted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Sincronización de posición y rotación (el NetworkTransform no funciona bien con la escala de mesa)
        private NetworkVariable<Vector3> m_NetPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<float> m_NetRotationY = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<bool> m_NetIsWalking = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<bool> m_NetUIVisible = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private void Awake()
        {
            if (m_AlertAudioSource == null)
                m_AlertAudioSource = GetComponent<AudioSource>();
        }

        private int m_CurrentWaypointIndex = 0;
        private bool m_IsStopped = true;
        private bool m_IsOnCooldown = false;
        private bool m_IsLocalAlertPending = false;
        private bool m_HasNetworkData = false;
        private Coroutine m_ResumeMovementCoroutine;
        private Coroutine m_EndOfRouteCoroutine;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (SafetyGameManager.Instance != null)
                SafetyGameManager.Instance.RegisterNPC(this);

            if (IsServer)
            {
                isInDanger.Value = false;
                isAlerted.Value = false;
                m_NetPosition.Value = transform.position;
                m_NetRotationY.Value = transform.eulerAngles.y;
                m_NetIsWalking.Value = false;
                m_NetUIVisible.Value = false;
                m_HasNetworkData = true;
                if (m_Animator != null) m_Animator.SetBool("IsWalking", false);
            }
            else
            {
                transform.position = m_NetPosition.Value;
                m_HasNetworkData = true;

                m_NetPosition.OnValueChanged += (_, _) => m_HasNetworkData = true;
                m_NetIsWalking.OnValueChanged += (_, current) =>
                {
                    if (m_Animator != null) m_Animator.SetBool("IsWalking", current);
                };
                if (m_Animator != null) m_Animator.SetBool("IsWalking", m_NetIsWalking.Value);
            }

            // Todos (servidor y clientes) reaccionan al cambio de visibilidad de la UI.
            // El sonido se reproduce aquí (una sola vez por ciclo) en lugar de desde eventos de hover,
            // para que el cooldown del código lo controle completamente.
            m_NetUIVisible.OnValueChanged += (_, visible) =>
            {
                SetUIContentVisible(visible);
                if (visible)
                    PlayAlertSound();
                else
                    m_IsLocalAlertPending = false;
            };
            SetUIContentVisible(m_NetUIVisible.Value);
        }

        /// <summary>
        /// Asigna la ruta de waypoints y pone al NPC en marcha. Solo servidor.
        /// </summary>
        public void InitializeRoute(Transform[] waypoints)
        {
            if (!IsServer) return;

            m_Waypoints = waypoints;
            m_CurrentWaypointIndex = 0;

            if (m_Waypoints != null && m_Waypoints.Length > 0)
            {
                m_IsStopped = false;
                m_NetIsWalking.Value = true;
                if (m_Animator != null) m_Animator.SetBool("IsWalking", true);
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                if (!m_IsStopped && m_Waypoints != null && m_Waypoints.Length > 0)
                    MoveTowardsWaypoint();

                m_NetPosition.Value = transform.position;
                m_NetRotationY.Value = transform.eulerAngles.y;
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
            // Mantener al NPC en su propio plano de altura
            targetPosition.y = transform.position.y;

            Vector3 moveDirection = (targetPosition - transform.position).normalized;
            transform.position += moveDirection * m_MovementSpeed * Time.deltaTime;

            if (moveDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 6f);
            }

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
            if (isAlerted.Value || m_IsOnCooldown || m_IsLocalAlertPending) return;

            m_IsLocalAlertPending = true;

            if (IsOwner)
                ApplyAlert();
            else
                AlertNPCServerRpc();
        }

        [Rpc(SendTo.Server)]
        private void AlertNPCServerRpc()
        {
            if (isAlerted.Value || m_IsOnCooldown) return;
            ApplyAlert();
        }

        private void ApplyAlert()
        {
            if (!IsServer) return;

            isAlerted.Value = true;
            m_IsStopped = true;
            m_NetIsWalking.Value = false;
            m_NetUIVisible.Value = true;
            if (m_Animator != null) m_Animator.SetBool("IsWalking", false);

            if (m_ResumeMovementCoroutine != null) StopCoroutine(m_ResumeMovementCoroutine);
            m_ResumeMovementCoroutine = StartCoroutine(ResumeMovementRoutine());
        }

        private IEnumerator ResumeMovementRoutine()
        {
            yield return new WaitForSeconds(m_AlertDuration);

            if (!IsServer) yield break;

            m_IsStopped = false;
            isAlerted.Value = false;
            m_NetIsWalking.Value = true;
            m_NetUIVisible.Value = false;
            if (m_Animator != null) m_Animator.SetBool("IsWalking", true);

            // Cooldown silencioso: el NPC ya camina y la UI está oculta, pero no se permiten nuevas alertas.
            m_IsOnCooldown = true;
            yield return new WaitForSeconds(m_AlertCooldown);
            m_IsOnCooldown = false;
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
            m_NetIsWalking.Value = false;
            if (m_Animator != null) m_Animator.SetBool("IsWalking", false);
            if (m_ResumeMovementCoroutine != null) StopCoroutine(m_ResumeMovementCoroutine);
            if (m_EndOfRouteCoroutine != null) StopCoroutine(m_EndOfRouteCoroutine);
        }

        /// <summary>
        /// Cambia el estado de peligro del NPC.
        /// </summary>
        public void SetInDanger(bool state)
        {
            if (!IsServer) return;

            isInDanger.Value = state;
        }

        private void SetUIContentVisible(bool visible)
        {
            if (m_AlertUIContent != null)
                m_AlertUIContent.SetActive(visible);
        }

        private void PlayAlertSound()
        {
            if (m_AlertAudioSource != null && !m_AlertAudioSource.isPlaying)
                m_AlertAudioSource.Play();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (m_Waypoints == null || m_Waypoints.Length == 0) return;

            float scale = transform.lossyScale.x;
            float sphereRadius = scale * 1.2f;

            for (int i = 0; i < m_Waypoints.Length; i++)
            {
                if (m_Waypoints[i] == null) continue;

                Vector3 pos = m_Waypoints[i].position;

                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(pos, sphereRadius);

                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(pos + Vector3.up * sphereRadius * 2f, $"{i + 1}");

                int next = (i + 1) % m_Waypoints.Length;
                if (m_Waypoints[next] != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(pos, m_Waypoints[next].position);
                }
            }
        }
#endif
    }
}
