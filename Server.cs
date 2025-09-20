using UnityEngine;
using Unity.Services.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Unity.Services.CloudSave;

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
            await RegisterWorldToCloudSave(WorldName, transport.ConnectionData.Address, transport.ConnectionData.Port);
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

    private async Task RegisterWorldToCloudSave(string worldName, string ip, ushort port)
    {
        if (useLocalServer)
        {
            Debug.Log("[Server] Skipping Firebase registration when hosted locally.");
            return;
        }

        try
        {
            var info = new WorldInfo
            {
                ip = ip,
                port = port,
                createdAt = DateTime.UtcNow.ToString("o")
            };

            string json = JsonUtility.ToJson(info);

            // Cloud Save API expects Dictionary<string,string>
            var dict = new Dictionary<string, object>
            {
                {$"worlds/{worldName}", json}
            };

            await CloudSaveService.Instance.Data.Player.SaveAsync(dict);

            Debug.Log($"[Server] World '{worldName}' registered in CloudSave at {ip}:{port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Server] Failed to register world in CloudSave: {e}");
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

    public static async Task<(string ip, ushort port)> LoadServerEndpoint()
    {
        try
        {
            var keys = new HashSet<string> { "ip", "port" };
            var results = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

            string ip = results["ip"].Value.GetAsString();
            ushort port = ushort.Parse(results["port"].Value.GetAsString());

            Debug.Log($"[Server] Loaded server endpoint: {ip}:{port}");
            return (ip, port);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Server] Failed to load server endpoint from CloudSave: {e}");
            return ("127.0.0.1", 7777); // fallback for local testing
        }
    }

    [Serializable]
    public class WorldInfo
    {
        public string ip;
        public ushort port;
        public string createdAt;
    }
}