using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class WorldSelectUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject uiParent;
    public TMP_InputField worldInput;
    public Button joinButton;
    public TextMeshProUGUI statusText; // Optional: show "Connecting..." here

    void Start()
    {
        joinButton.onClick.AddListener(OnJoinClicked);

        // Allow Enter key to trigger join
        worldInput.onSubmit.AddListener(_ => OnJoinClicked());

        // Optional: focus the input field at start
        worldInput.ActivateInputField();
    }

    void OnJoinClicked()
    {
        string worldName = worldInput.text.Trim();
        if (string.IsNullOrEmpty(worldName))
        {
            Debug.LogWarning("World name is empty!");
            if (statusText) statusText.text = "Enter a world name!";
            return;
        }

        // Hide UI while connecting
        uiParent.SetActive(false);
        
        if(statusText) statusText.text = $"Connecting to {worldName}...";

        Client.Instance.JoinWorld(worldName);

        // Hook into disconnect events so UI comes back if it fails
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if(clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.LogWarning("[UI] Failed to join world, showing UI again");
            uiParent.SetActive(true);
            if (statusText) statusText.text = "Failed to connect. Try another world.";
        }
    }
}
