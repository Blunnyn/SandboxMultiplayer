using Unity.Netcode;
using UnityEngine;
using XRMultiplayer.SafetyGame;

namespace UnityEngine.XR.Templates.MRTTabletopAssets
{
    /// <summary>
    /// Modo de juego para el minijuego de seguridad y riesgos en construcción civil.
    /// Implementa IGameMode para integrarse con el GameModeManager del tablero.
    /// </summary>
    public class SafetyGameMode : NetworkBehaviour, IGameMode
    {
        [Header("Configuración del Modo de Juego")]
        [SerializeField] private int m_GameModeID = 5;

        [Tooltip("El contenedor principal de los elementos del minijuego (terreno, NPCs, zonas) que se activará o desactivará.")]
        public GameObject gameModeContainer;

        [Tooltip("Dispensador de conos del minijuego. Necesario para reiniciar sus corrutinas al mostrar el modo.")]
        public NetworkObjectDispenser coneDispenser;

        /// <summary>
        /// ID del modo de juego para el GameModeManager.
        /// </summary>
        public int gameModeID => m_GameModeID;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Garantizar que iniciamos en el estado correcto (oculto)
            if (gameModeContainer != null)
            {
                gameModeContainer.SetActive(false);
            }
        }

        /// <summary>
        /// Oculta los elementos visuales del escenario al cambiar de juego.
        /// </summary>
        public void HideGameMode()
        {
            Debug.Log($"[Safety Game Mode] Ocultando el modo de juego (Servidor: {IsServer}, Cliente ID: {NetworkManager.Singleton.LocalClientId})");

            if (coneDispenser != null && coneDispenser.gameObject.activeInHierarchy)
            {
                coneDispenser.Hide();
            }

            if (gameModeContainer != null)
            {
                gameModeContainer.SetActive(false);
            }

            OnGameModeEnd();
        }

        /// <summary>
        /// Muestra el escenario cuando el juego es seleccionado en la mesa virtual.
        /// </summary>
        public void ShowGameMode()
        {
            Debug.Log($"[Safety Game Mode] Mostrando el modo de juego (Servidor: {IsServer}, Cliente ID: {NetworkManager.Singleton.LocalClientId})");
            if (gameModeContainer != null)
            {
                gameModeContainer.SetActive(true);
            }

            if (coneDispenser != null)
            {
                coneDispenser.Show();
            }

            OnGameModeStart();
        }

        /// <summary>
        /// Inicializa el estado del juego cuando comienza formalmente la partida.
        /// </summary>
        public void OnGameModeStart()
        {
            Debug.Log($"[Safety Game Mode] OnGameModeStart invocado. (Servidor: {IsServer})");
            // Solo el servidor inicializa y activa el juego en red
            if (!IsServer) return;

            if (SafetyGameManager.Instance != null)
            {
                SafetyGameManager.Instance.isGameActive.Value = true;
                // Restablecer valores de la partida
                SafetyGameManager.Instance.timeRemaining.Value = 180f; // Duración por defecto
                SafetyGameManager.Instance.score.Value = 1000;         // Puntos iniciales
                Debug.Log("[Safety Game Mode] Minijuego de seguridad iniciado en el servidor.");
            }
            else
            {
                Debug.LogWarning("[Safety Game Mode] No se pudo iniciar el juego: SafetyGameManager.Instance es nulo.");
            }
        }

        /// <summary>
        /// Detiene la simulación del minijuego cuando termina o se cambia de juego.
        /// </summary>
        public void OnGameModeEnd()
        {
            Debug.Log($"[Safety Game Mode] OnGameModeEnd invocado. (Servidor: {IsServer})");
            if (!IsServer) return;

            if (SafetyGameManager.Instance != null)
            {
                SafetyGameManager.Instance.isGameActive.Value = false;
                Debug.Log("[Safety Game Mode] Minijuego de seguridad finalizado en el servidor.");
            }
        }
    }
}
