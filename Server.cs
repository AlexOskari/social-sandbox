using UnityEngine;
using Unity.Services.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Unity.Services.CloudSave;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using Newtonsoft.Json;

public class Server : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    public string WorldName { get; private set; }

    [Header("Local Testing Settings")]
    public bool useLocalServer = false;        // Enable this for local testing
    public string localHost = "127.0.0.1";     // Usually localhost
    public ushort localPort = 7777;            // Port for local testing

    private UnityTransport transport;

    const string SupabaseUrl = "https://znfcfvrrsokbqymcbrsr.supabase.co";

    async void Awake()
    {
        // Make sure this is a server
        if (!Environment.GetCommandLineArgs().Any(arg => arg == "-launch-as-server"))
            return;

        Debug.Log("Server Scene Opened!");

        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[Server] No UnityTransport found on NetworkManager!");
            return;
        }

        Debug.Log("[Server] Initializing Supabase for server...");
        try
        {
            if (useLocalServer)
            {
                // Local testing: no need to read or write to Supabase database
                Debug.Log("[Server] Skipping Supabase initialization for local testing");
            }
            else
            {
                await InitializeSupabase();
            }
        } 
        catch (Exception e)
        {
            Debug.LogError($"[Server] Failed to initialize Supabase: {e}");
        }

        Debug.Log("[Server] Initializing Unity Services for server...");
        try
        {
            await UnityServices.InitializeAsync();
            Debug.Log("[Server] Unity Services initialized");

            if (useLocalServer)
            {
                // Local testing: manually set transport data
                Debug.Log($"[Server] Starting LOCAL server on {localHost}:{localPort}");
                transport.ConnectionData.Address = localHost;
                transport.ConnectionData.Port = localPort;
            }
            else
            {
                // MPS deployment: use allocated port automatically
                Debug.Log("[Server] Starting MPS dedicated server");
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

            // Register to CloudSave
            await RegisterWorldToCloudSave(WorldName, transport.ConnectionData.Address, transport.ConnectionData.Port);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Server] Failed to initialize Unity Services: {e}");
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
            Debug.Log("[Server] Skipping CloudSave registration when hosted locally.");
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

    private async Task<Supabase.Client> InitializeSupabase()
    {
        // Search for supabase service key in launch parameters
        string[] args = Environment.GetCommandLineArgs();
        string supabaseKey = "";
        foreach(var arg in args)
        {
            if (arg.StartsWith("-supabaseKey="))
                supabaseKey = arg.Substring("-supabaseKey=".Length);
        }

        // If the key wasn't found:
        if (supabaseKey == "")
        {
            Debug.LogError("[Server] Couldn't find supabase service key in launch parameters!");
            return null;
        }

        // Initialize supabase client for server
        var clientOptions = new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = true
        };

        var client = new Supabase.Client(SupabaseUrl, supabaseKey, clientOptions);
        await client.InitializeAsync();
        Debug.Log("[Server] Supabase initialized successfully.");
        return client;
    }

    [Serializable]
    public class WorldInfo
    {
        public string ip;
        public ushort port;
        public string createdAt;
    }
}