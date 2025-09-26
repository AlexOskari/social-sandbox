using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using System.Collections.Generic;
using System;
using Unity.Services.CloudSave;

public class Client : MonoBehaviour
{
    public static Client Instance;

    [Header("Local Testing Settings")]
    public bool useLocalServer = false;        // Enable this for local testing
    public string localHost = "127.0.0.1";     // Usually localhost
    public ushort localPort = 7777;            // Port for local testing

    private UnityTransport transport;

    async void Awake()
    {
        if (Instance == null) Instance = this;

        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[Client] No UnityTransport found on NetworkManager!");
            return;
        }

        Debug.Log("[Client] Initializing Unity Services...");
        await InitUGS();

        // Don't auto-connect to server here!
    }

    private async Task InitUGS()
    {
        try
        {
            await UnityServices.InitializeAsync();
            Debug.Log("[Client] Unity Services initialized");

            // Optional: if you need Unity Authentication too
            //if (!AuthenticationService.Instance.IsSignedIn)
            //{
            //    await AuthenticationService.Instance.SignInAnonymouslyAsync();
            //    Debug.Log("[ClientInitializer] Signed in to Unity Authentication");
            //}
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Client] Failed to initialize Unity Services: {e}");
        }
    }

    public async Task JoinWorld(string worldName)
    {
        Debug.Log($"[Client] Requesting to join world: {worldName}");

        if (useLocalServer)
        {
            // Local test: just connect directly
            Debug.Log($"Connecting to LOCAL server {localHost}:{localPort}");
            transport.SetConnectionData(localHost, localPort);
        }
        else
        {
            // Ask CloudSave where this world lives
            Debug.Log("[Client] Loading world info from CloudSave...");
            var results = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { "ip", "port" });
            var info = await LoadWorldInfo(worldName);

            if (info == null)
            {
                Debug.LogError($"[Client] World '{worldName}' not found in CloudSave.");
                return;
            }

            Debug.Log($"[Client] Connecting to {worldName} at {info.Ip}:{info.Port}");
            transport.SetConnectionData(info.Ip, (ushort)info.Port);
        }

        NetworkManager.Singleton.StartClient();

        NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
        {
            if (id == NetworkManager.Singleton.LocalClientId)
            {
                using var writer = new FastBufferWriter(256, Allocator.Temp);
                writer.WriteValueSafe(worldName);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(WorldMessages.JoinWorld, NetworkManager.ServerClientId, writer);
            }
        };
    }

    private async Task<Server.WorldRow> LoadWorldInfo(string worldName)
    {
        try
        {
            var key = $"worlds/{worldName}";
            var keys = new HashSet<string> { key };
            var results = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

            if (!results.TryGetValue(key, out var item) || item == null)
                return null;

            // results[key] is an Item; the stored JSON string is in Item.Value
            string json = item.Value?.ToString();
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"[Client] Cloud Save item for key '{key}' empty or null.");
                return null;
            }
            var info = JsonUtility.FromJson<Server.WorldRow>(json);
            return info;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Client] Failed to load world info: {e}");
            return null;
        }
    }
}