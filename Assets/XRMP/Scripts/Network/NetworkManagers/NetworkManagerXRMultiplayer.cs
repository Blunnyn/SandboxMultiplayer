using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace XRMultiplayer
{
    /// <summary>
    /// Manages the network functionality for VR multiplayer.
    /// </summary>
    public class NetworkManagerXRMultiplayer : NetworkManager
    {
        [SerializeField, Tooltip("Set this to control how much logging is generated")]
        LogLevel m_LogLevel;

        [SerializeField, Tooltip("This should almost always be set to true")]
        bool m_RunInBackground = true;

        [SerializeField]
        NetworkConfig m_NetworkConfig;

        ///<inheritdoc/>
        void Awake()
        {
            LogLevel = m_LogLevel;
            RunInBackground = m_RunInBackground;
            NetworkConfig = m_NetworkConfig;
            Utils.s_LogLevel = LogLevel;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(NetworkManagerXRMultiplayer))]
    class VRMutliplayerTemplateNetworkManagerEditor : Editor
    {
        /// <summary>
        /// This function is called when the inspector is drawn.
        /// </summary>
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (Application.isPlaying)
            {
                switch (XRINetworkGameManager.CurrentConnectionState.Value)
                {
                    case XRINetworkGameManager.ConnectionState.None:
                        GUILayout.Box("Autenticando");
                        break;
                    case XRINetworkGameManager.ConnectionState.Authenticating:
                        GUILayout.Box("Autenticando");
                        break;
                    case XRINetworkGameManager.ConnectionState.Authenticated:
                        if (GUILayout.Button("Conectar"))
                        {
                            XRINetworkGameManager.Instance.QuickJoinLobby();
                        }
                        break;
                    case XRINetworkGameManager.ConnectionState.Connecting:
                        GUILayout.Box("Conectando");
                        break;
                    case XRINetworkGameManager.ConnectionState.Connected:
                        if (GUILayout.Button("Desconectar"))
                        {
                            XRINetworkGameManager.Instance.Disconnect();
                        }
                        break;
                }
            }
            else
            {
                GUILayout.Box("El juego no está corriendo.");
                GUI.enabled = false;
                GUILayout.Button("Conectar");
                GUI.enabled = true;
            }
        }
    }
#endif
}
