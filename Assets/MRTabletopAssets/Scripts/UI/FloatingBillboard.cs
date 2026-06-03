using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Componente reutilizable para un Canvas World Space que puede:
    /// 1. Mirar siempre hacia la cámara del jugador (billboard suave).
    /// 2. Flotar de arriba a abajo con una animación sinusoidal.
    /// Ambos efectos se pueden activar/desactivar de forma independiente.
    /// </summary>
    public class FloatingBillboard : MonoBehaviour
    {
        [Header("Billboard")]
        [Tooltip("Si está activo, el objeto rotará suavemente para mirar hacia la cámara principal.")]
        [SerializeField] private bool m_FaceCamera = true;
        [SerializeField] private float m_RotationSpeed = 8f;

        [Header("Efecto flotante")]
        [Tooltip("Si está activo, el objeto oscilará verticalmente en su espacio local.")]
        [SerializeField] private bool m_FloatEffect = true;
        [SerializeField] private float m_FloatAmplitude = 0.5f;
        [SerializeField] private float m_FloatSpeed = 1.2f;

        private Camera m_Camera;
        private Vector3 m_InitialLocalPosition;

        private void Awake()
        {
            m_Camera = Camera.main;
        }

        private void Start()
        {
            m_InitialLocalPosition = transform.localPosition;
        }

        private void Update()
        {
            if (m_FaceCamera)
                UpdateBillboard();

            if (m_FloatEffect)
                UpdateFloat();
            else
                transform.localPosition = m_InitialLocalPosition;
        }

        private void UpdateBillboard()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                return;
            }

            // El canvas muestra su contenido en -Z, así que su forward (+Z) apunta
            // OPUESTO a la cámara para que el contenido sea visible al jugador.
            Vector3 dirAwayFromCamera = (transform.position - m_Camera.transform.position).normalized;
            if (dirAwayFromCamera == Vector3.zero) return;

            Quaternion targetRotation = Quaternion.LookRotation(dirAwayFromCamera, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * m_RotationSpeed);
        }

        private void UpdateFloat()
        {
            Vector3 pos = m_InitialLocalPosition;
            pos.y += Mathf.Sin(Time.time * m_FloatSpeed) * m_FloatAmplitude;
            transform.localPosition = pos;
        }

        /// <summary>
        /// Activa o desactiva el efecto billboard desde código en runtime.
        /// </summary>
        public void SetFaceCamera(bool enabled) => m_FaceCamera = enabled;

        /// <summary>
        /// Activa o desactiva el efecto flotante desde código en runtime.
        /// </summary>
        public void SetFloatEffect(bool enabled) => m_FloatEffect = enabled;

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_FloatAmplitude = Mathf.Max(0f, m_FloatAmplitude);
            m_FloatSpeed = Mathf.Max(0f, m_FloatSpeed);
            m_RotationSpeed = Mathf.Max(0f, m_RotationSpeed);
        }
#endif
    }
}
