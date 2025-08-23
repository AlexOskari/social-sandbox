using UnityEngine;
using Unity.Services.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Firebase.Database;

public class Server : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    public string WorldName { get; private set; }

    [Header("Local Testing Settings")]
    public bool useLocalServer = false;        // Enable this for local testing
    public string localHost = "127.0.0.1";     // Usually localhost
    public ushort localPort = 7777;            // Port for local testing

    private UnityTransport transport;

    async void Awake()
    {
        Debug.Log("Server Scene Opened!");

        //!System.Environment.GetCommandLineArgs().Any(arg => arg == "-port") || 
        if (!System.Environment.GetCommandLineArgs().Any(arg => arg == "-launch-as-server"))
            return;

        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[Server] No UnityTransport found on NetworkManager!");
            return;
        }

        Debug.Log("Initializing Unity Services for Server...");
        try
        {
            await UnityServices.InitializeAsync();
            Debug.Log("Unity Services initialized");

            if (useLocalServer)
            {
                // Local testing: manually set transport data
                Debug.Log($"Starting LOCAL server on {localHost}:{localPort}");
                transport.ConnectionData.Address = localHost;
                transport.ConnectionData.Port = localPort;
            }
            else
            {
                // MPS deployment: use allocated port automatically
                Debug.Log("Starting MPS dedicated server");
                // Optional: you can access fleet allocation if needed
                // var allocation = await MultiplayService.Instance.GetFleetAllocationAsync();
            }

            WorldName = "DefaultWorld";
            foreach (string arg in System.Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("-world="))
                    WorldName = arg.Substring(7);
            }

            Debug.Log($"[Server] Starting world: {WorldName}");
            
            // Start server
            NetworkManager.Singleton.StartServer();
            
            // Register to Firebase
            await RegisterWorldToFirebase(WorldName, transport.ConnectionData.Address, transport.ConnectionData.Port);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Server] Failed to initialize: {e}");
        }
    }

    private void Start()
    {
        // Register message handler
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(WorldMessages.JoinWorld, OnJoinWorldRequest);
    }

    private async Task RegisterWorldToFirebase(string worldName, string ip, ushort port)
    {
        if (useLocalServer)
        {
            Debug.Log("[Server] Skipping Firebase registration when hosted locally.");
            return;
        }

        try
        {
            var worldData = new Dictionary<string, object>
            {
                { "ip", ip },
                { "port", port },
                { "createdAt", ServerValue.Timestamp }
            };

            var dbRef = FirebaseDatabase.DefaultInstance.RootReference.Child("Worlds").Child(worldName);
            await dbRef.SetValueAsync(worldData);
            Debug.Log($"[Server] World '{worldName}' registered in Firebase at {ip}:{port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Server] Failed to register world in Firebase : {e}");
        }
    }

    private void OnJoinWorldRequest(ulong clientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string requestedWorld);

        Debug.Log($"[Server] Client {clientId} requested world: {requestedWorld}");

        if (requestedWorld != WorldName)
        {
            Debug.Log($"Rejecting {clientId}, world mismatch! Request: {requestedWorld} but this server is {WorldName}");
            // Optionally disconnect the client:
            NetworkManager.Singleton.DisconnectClient(clientId);
            return;
        }

        // Otherwise accept and spawn player
        var player = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

        Debug.Log($"[Server] {clientId} joined world {WorldName}");
    }
}
