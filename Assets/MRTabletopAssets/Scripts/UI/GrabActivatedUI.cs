using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace XRMultiplayer
{
    /// <summary>
    /// Activa o desactiva un GameObject de UI cuando el jugador local agarra un objeto
    /// cuyo tag coincide con <see cref="m_ActivationTag"/>.
    ///
    /// Coloca este componente en el Canvas (no en el hijo Image).
    /// Asigna el Image (u otro hijo con el contenido) en <see cref="m_UITarget"/>.
    /// </summary>
    public class GrabActivatedUI : MonoBehaviour
    {
        [Tooltip("Tag del objeto agarrable que activa esta UI (p.ej. SafetyCone, SafetyBarrier).")]
        [SerializeField] private string m_ActivationTag;

        [Tooltip("GameObject a activar/desactivar. Si está vacío, se usa este mismo GameObject.")]
        [SerializeField] private GameObject m_UITarget;

        private XRInteractionManager m_InteractionManager;
        private readonly List<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable> m_TrackedInteractables = new();
        private int m_ActiveGrabCount;

        private void Awake()
        {
            if (m_UITarget == null)
                m_UITarget = gameObject;
        }

        private void OnEnable()
        {
            m_InteractionManager = FindObjectOfType<XRInteractionManager>();
            if (m_InteractionManager == null)
            {
                Debug.LogWarning("[GrabActivatedUI] No se encontró XRInteractionManager en la escena.", this);
                return;
            }

            m_InteractionManager.interactableRegistered += OnInteractableRegistered;
            m_InteractionManager.interactableUnregistered += OnInteractableUnregistered;

            // Suscribirse a los interactables ya registrados antes de que este componente se activara.
            var existing = new List<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable>();
            m_InteractionManager.GetRegisteredInteractables(existing);
            foreach (var interactable in existing)
                TrySubscribe(interactable);

            m_ActiveGrabCount = 0;
            m_UITarget.SetActive(false);
        }

        private void OnDisable()
        {
            if (m_InteractionManager != null)
            {
                m_InteractionManager.interactableRegistered -= OnInteractableRegistered;
                m_InteractionManager.interactableUnregistered -= OnInteractableUnregistered;
            }

            foreach (var interactable in m_TrackedInteractables)
            {
                if (interactable == null) continue;
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
            }
            m_TrackedInteractables.Clear();
        }

        private void OnInteractableRegistered(InteractableRegisteredEventArgs args)
            => TrySubscribe(args.interactableObject);

        private void OnInteractableUnregistered(InteractableUnregisteredEventArgs args)
        {
            if (args.interactableObject is not UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable selectable) return;
            if (!m_TrackedInteractables.Remove(selectable)) return;

            selectable.selectEntered.RemoveListener(OnSelectEntered);
            selectable.selectExited.RemoveListener(OnSelectExited);
        }

        private void TrySubscribe(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
        {
            if (string.IsNullOrEmpty(m_ActivationTag)) return;
            if (interactable is not UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable selectable) return;
            if (!interactable.transform.CompareTag(m_ActivationTag)) return;

            selectable.selectEntered.AddListener(OnSelectEntered);
            selectable.selectExited.AddListener(OnSelectExited);
            m_TrackedInteractables.Add(selectable);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_ActiveGrabCount++;
            m_UITarget.SetActive(true);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            m_ActiveGrabCount = Mathf.Max(0, m_ActiveGrabCount - 1);
            if (m_ActiveGrabCount == 0)
                m_UITarget.SetActive(false);
        }
    }
}
