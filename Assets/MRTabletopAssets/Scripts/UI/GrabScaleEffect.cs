using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRMultiplayer
{
    /// <summary>
    /// Multiplica la escala de un objeto agarrable mientras está siendo sujetado por el jugador
    /// (no por un socket) y la restaura al soltarlo.
    ///
    /// Requiere desactivar <see cref="XRGrabInteractable.trackScale"/>: con Track Scale activado,
    /// el grab transformer reescribe la escala cada frame y pisa este efecto.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class GrabScaleEffect : MonoBehaviour
    {
        [SerializeField] private float m_GrabScaleMultiplier = 3f;

        private XRGrabInteractable m_Interactable;
        private Vector3 m_OriginalLocalScale;
        private bool m_CapturedOriginal;

        private void Start()
        {
            // Capturar en Start: el InteractableSpawner sobreescribe localScale tras el spawn
            // y NetworkBaseInteractable la fija en OnNetworkSpawn; ambos corren antes de Start.
            m_OriginalLocalScale = transform.localScale;
            m_CapturedOriginal = true;
        }

        private void OnEnable()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();

            // Impide que el sistema de agarre controle la escala (de lo contrario pisa este efecto).
            m_Interactable.trackScale = false;

            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }

        private void OnDisable()
        {
            if (m_Interactable == null) return;
            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            if (!m_CapturedOriginal) return;
            transform.localScale = m_OriginalLocalScale * m_GrabScaleMultiplier;
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            if (!m_CapturedOriginal) return;
            transform.localScale = m_OriginalLocalScale;
        }
    }
}
