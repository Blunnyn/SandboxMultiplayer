using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Fija este objeto a la cámara del jugador (head-locked): se mueve y gira
    /// perfectamente con la cabeza, como pegado a la pantalla/visor.
    ///
    /// A diferencia de LazyFollow (que recoloca la UI solo cuando giras lo suficiente),
    /// esto actualiza la pose en onBeforeRender, justo antes de renderizar, eliminando
    /// el lag/"swim" del seguimiento. Reemplaza al LazyFollow en la UI: no uses ambos.
    /// </summary>
    public class HeadLockedUI : MonoBehaviour
    {
        [SerializeField] private Camera m_Camera;

        [Tooltip("Desplazamiento respecto a la cámara, en su espacio local. x: derecha, y: arriba, z: adelante (distancia).")]
        [SerializeField] private Vector3 m_Offset = new Vector3(0.12f, 0.08f, 0.5f);

        [Tooltip("Rotación extra respecto a mirar de frente desde la cámara.")]
        [SerializeField] private Vector3 m_RotationOffset = Vector3.zero;

        [Tooltip("0 = pegado perfecto (sin lag). >0 = suavizado opcional para un movimiento más blando.")]
        [SerializeField] private float m_Smoothing = 0f;

        private void Awake()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;
        }

        private void OnEnable()
        {
            Application.onBeforeRender += UpdatePose;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= UpdatePose;
        }

        private void UpdatePose()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null) return;
            }

            Transform cam = m_Camera.transform;
            Vector3 targetPos = cam.position + cam.rotation * m_Offset;
            Quaternion targetRot = cam.rotation * Quaternion.Euler(m_RotationOffset);

            if (m_Smoothing > 0f)
            {
                float t = 1f - Mathf.Exp(-m_Smoothing * Time.deltaTime);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, targetPos, t),
                    Quaternion.Slerp(transform.rotation, targetRot, t));
            }
            else
            {
                // Pegado perfecto: la UI ocupa siempre el mismo punto de la vista.
                transform.SetPositionAndRotation(targetPos, targetRot);
            }
        }
    }
}
