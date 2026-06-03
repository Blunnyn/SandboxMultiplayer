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

        [Header("Bloqueo de ejes (Billboard)")]
        [Tooltip("Bloquea el eje X: el objeto no se inclinará arriba/abajo hacia la cámara.")]
        [SerializeField] private bool m_LockX = false;
        [Tooltip("Bloquea el eje Y: el objeto no girará horizontalmente hacia la cámara.")]
        [SerializeField] private bool m_LockY = false;
        [Tooltip("Bloquea el eje Z: el objeto no rotará lateralmente.")]
        [SerializeField] private bool m_LockZ = false;

        [Header("Efecto flotante")]
        [Tooltip("Si está activo, el objeto oscilará verticalmente en su espacio local.")]
        [SerializeField] private bool m_FloatEffect = true;
        [SerializeField] private float m_FloatAmplitude = 0.5f;
        [SerializeField] private float m_FloatSpeed = 1.2f;

        private Camera m_Camera;
        private Vector3 m_InitialLocalPosition;
        private Vector3 m_LockedEuler;

        private void Awake()
        {
            m_Camera = Camera.main;
        }

        private void Start()
        {
            m_InitialLocalPosition = transform.localPosition;
            m_LockedEuler = transform.eulerAngles;
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
            Vector3 dir = (transform.position - m_Camera.transform.position).normalized;
            if (dir == Vector3.zero) return;

            // Lock X: se aplana la dirección al plano horizontal ANTES de calcular LookRotation.
            // Reemplazar el Euler X del resultado no funciona porque los Euler son interdependientes.
            if (m_LockX)
            {
                dir.y = 0f;
                if (dir == Vector3.zero) return;
                dir.Normalize();
            }

            Quaternion targetRotation = Quaternion.LookRotation(dir, Vector3.up);

            // Lock Y y Lock Z se aplican sobre los Euler del resultado.
            // Son aceptables aquí porque Y (yaw) y Z (roll) están menos acoplados en el orden ZXY de Unity.
            if (m_LockY || m_LockZ)
            {
                Vector3 e = targetRotation.eulerAngles;
                if (m_LockY) e.y = m_LockedEuler.y;
                if (m_LockZ) e.z = m_LockedEuler.z;
                targetRotation = Quaternion.Euler(e);
            }

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
