using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace XRMultiplayer
{
    public class NearCasterGizmo : MonoBehaviour
    {
        [SerializeField] private SphereInteractionCaster m_NearCaster;
        [SerializeField] private Color m_GizmoColor = new Color(0f, 1f, 0.5f, 0.4f);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (m_NearCaster == null)
                m_NearCaster = GetComponent<SphereInteractionCaster>();
            if (m_NearCaster == null) return;

            Gizmos.color = m_GizmoColor;
            Gizmos.DrawWireSphere(transform.position, m_NearCaster.castRadius);
            Gizmos.color = new Color(m_GizmoColor.r, m_GizmoColor.g, m_GizmoColor.b, 0.08f);
            Gizmos.DrawSphere(transform.position, m_NearCaster.castRadius);
        }
#endif
    }
}
