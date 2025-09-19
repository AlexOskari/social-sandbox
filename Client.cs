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
            //var snapshot = await FirebaseDatabase.DefaultInstance.RootReference.Child("Worlds").Child(worldName).GetValueAsync();
            var results = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { "ip", "port" });
            bool ipExists = results.ContainsKey("ip");
            bool portExists = results.ContainsKey("port");

            if (ipExists && portExists)
            {
                // Load the server ip and port from CloudSave
                var (ip, port) = await Server.LoadServerEndpoint();

                // Apply the loaded endpoint to the Unity Transport
                Debug.Log($"[Client] Connecting to {worldName} at {ip}:{port}");
                transport.SetConnectionData(ip, port);
            }
            else
            {
                Debug.LogWarning($"[Client] World {worldName} not found in CloudSave! (TODO: request new allocation");
                return;
            }
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

    //public async Task CreateWorld(string worldName, string ip, ushort port)
    //{
    //    if (firebaseAuth == null || firebaseAuth.CurrentUser == null)
    //    {
    //        Debug.LogError("[Client] Canot create world - not signet into Firebase!");
    //        return;
    //    }

    //    string userId = firebaseAuth.CurrentUser.UserId;

    //    var worldData = new Dictionary<string, object>
    //    {
    //        { "owner", userId },
    //        { "ip", ip },
    //        { "port", port },
    //        { "createdAt", ServerValue.Timestamp }
    //    };

    //    DatabaseReference dbRef = FirebaseDatabase.DefaultInstance.RootReference.Child("Worlds").Child(worldName);

    //    try
    //    {
    //        await dbRef.SetValueAsync(worldData);
    //        Debug.Log($"[Client] World '{worldName}' created and stored in Firebase");
    //    }
    //    catch (Exception e)
    //    {
    //        Debug.LogError($"[Client] Failed to create world: {e}");
    //    }
    //}
}