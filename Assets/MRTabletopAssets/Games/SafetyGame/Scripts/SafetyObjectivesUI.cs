using System.Collections;
using TMPro;
using UnityEngine;

namespace XRMultiplayer.SafetyGame
{
    /// <summary>
    /// Panel HUD que muestra el progreso de objetivos del minijuego de seguridad
    /// (cuántos conos y barreras se han colocado de los requeridos).
    /// Lee las NetworkVariables del SafetyGameManager y se refresca cuando cambian.
    ///
    /// Es un MonoBehaviour de presentación: no escribe estado de red, solo lo muestra.
    /// Pensado para vivir en un Canvas World Space con LazyFollow (HUD en esquina).
    /// </summary>
    public class SafetyObjectivesUI : MonoBehaviour
    {
        [Header("Textos")]
        [SerializeField] private TextMeshProUGUI m_ConesText;
        [SerializeField] private TextMeshProUGUI m_BarriersText;

        [Header("Etiquetas")]
        [SerializeField] private string m_ConesLabel = "Conos";
        [SerializeField] private string m_BarriersLabel = "Barreras";
        [Tooltip("Formato: {0}=etiqueta, {1}=colocados, {2}=requeridos.")]
        [SerializeField] private string m_Format = "{0}: {1}/{2}";

        [Header("Opcional")]
        [Tooltip("GameObject que se activa cuando TODOS los objetivos están completos (conos y barreras).")]
        [SerializeField] private GameObject m_CompletedIndicator;

        private SafetyGameManager m_Manager;

        private void OnEnable()
        {
            // Los GameObjects de los TMP quedaron con active=false del mecanismo anterior
            // (las listas de PlayerUIArea). Ahora su visibilidad la controla el padre (este
            // panel): nos aseguramos de que estén activos siempre que este componente lo esté.
            // OnEnable se dispara cada vez que el panel pasa a activo, así que se reaplica solo.
            if (m_ConesText != null) m_ConesText.gameObject.SetActive(true);
            if (m_BarriersText != null) m_BarriersText.gameObject.SetActive(true);

            StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            Unsubscribe();
            m_Manager = null;
        }

        private IEnumerator BindWhenReady()
        {
            // El manager se spawnea en runtime; esperamos a que exista y esté en red.
            while (SafetyGameManager.Instance == null || !SafetyGameManager.Instance.IsSpawned)
                yield return null;

            m_Manager = SafetyGameManager.Instance;

            m_Manager.conesPlaced.OnValueChanged += OnCountChanged;
            m_Manager.conesRequired.OnValueChanged += OnCountChanged;
            m_Manager.barriersPlaced.OnValueChanged += OnCountChanged;
            m_Manager.barriersRequired.OnValueChanged += OnCountChanged;

            Refresh();
        }

        private void Unsubscribe()
        {
            if (m_Manager == null) return;

            m_Manager.conesPlaced.OnValueChanged -= OnCountChanged;
            m_Manager.conesRequired.OnValueChanged -= OnCountChanged;
            m_Manager.barriersPlaced.OnValueChanged -= OnCountChanged;
            m_Manager.barriersRequired.OnValueChanged -= OnCountChanged;
        }

        private void OnCountChanged(int previous, int current) => Refresh();

        private void Refresh()
        {
            if (m_Manager == null) return;

            int conesPlaced = m_Manager.conesPlaced.Value;
            int conesRequired = m_Manager.conesRequired.Value;
            int barriersPlaced = m_Manager.barriersPlaced.Value;
            int barriersRequired = m_Manager.barriersRequired.Value;

            if (m_ConesText != null)
                m_ConesText.text = string.Format(m_Format, m_ConesLabel, conesPlaced, conesRequired);

            if (m_BarriersText != null)
                m_BarriersText.text = string.Format(m_Format, m_BarriersLabel, barriersPlaced, barriersRequired);

            if (m_CompletedIndicator != null)
            {
                bool allDone = conesPlaced >= conesRequired && barriersPlaced >= barriersRequired;
                m_CompletedIndicator.SetActive(allDone);
            }
        }
    }
}
