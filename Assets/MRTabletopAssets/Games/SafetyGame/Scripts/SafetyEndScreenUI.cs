using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Pantalla final del minijuego de seguridad. Muestra una Card de felicitación cuando
    /// se completan todos los objetivos (lee el latch <see cref="SafetyGameManager.objectivesCompleted"/>)
    /// y ofrece un botón para reiniciar el escenario.
    ///
    /// Es un MonoBehaviour de presentación: no escribe estado de red, solo lo lee y dispara
    /// el reinicio a través del manager (que lo resuelve en el servidor).
    /// </summary>
    public class SafetyEndScreenUI : MonoBehaviour
    {
        [Header("Pantalla final")]
        [Tooltip("Raíz de la Card de felicitación que se activa al completar los objetivos.")]
        [SerializeField] private GameObject m_EndCardRoot;

        [Tooltip("Botón que reinicia el escenario (limpia conos/barreras y el conteo).")]
        [SerializeField] private Button m_RestartButton;

        private SafetyGameManager m_Manager;

        private void OnEnable()
        {
            if (m_EndCardRoot != null)
                m_EndCardRoot.SetActive(false);

            if (m_RestartButton != null)
                m_RestartButton.onClick.AddListener(OnRestartPressed);

            StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            StopAllCoroutines();

            if (m_RestartButton != null)
                m_RestartButton.onClick.RemoveListener(OnRestartPressed);

            Unsubscribe();
            m_Manager = null;
        }

        private IEnumerator BindWhenReady()
        {
            // El manager se spawnea en runtime; esperamos a que exista y esté en red.
            while (SafetyGameManager.Instance == null || !SafetyGameManager.Instance.IsSpawned)
                yield return null;

            m_Manager = SafetyGameManager.Instance;
            m_Manager.objectivesCompleted.OnValueChanged += OnCompletedChanged;

            Refresh();
        }

        private void Unsubscribe()
        {
            if (m_Manager == null) return;
            m_Manager.objectivesCompleted.OnValueChanged -= OnCompletedChanged;
        }

        private void OnCompletedChanged(bool previous, bool current) => Refresh();

        private void Refresh()
        {
            if (m_Manager == null || m_EndCardRoot == null) return;
            m_EndCardRoot.SetActive(m_Manager.objectivesCompleted.Value);
        }

        private void OnRestartPressed()
        {
            if (m_Manager != null)
                m_Manager.RequestRestart();
        }
    }
}
