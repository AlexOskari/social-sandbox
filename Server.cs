using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using Newtonsoft.Json.Linq;
using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;


public class Server : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    public string WorldName { get; private set; }

    private Dictionary<string, WorldRow> worlds = new();

    [Header("Local Testing Settings")]
    public bool useLocalServer = false;        // Enable this for local testing
    public string localHost = "127.0.0.1";     // Usually localhost
    public ushort localPort = 7777;            // Port for local testing

    private UnityTransport transport;

    const string SUPABASE_URL = "https://znfcfvrrsokbqymcbrsr.supabase.co";
    private Supabase.Client _client;

    async void Awake()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => arg == "-launch-as-server")) return;

        Debug.Log("Server Scene Opened!");

        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[Server] No UnityTransport found on NetworkManager!");
            return;
        }

        worlds["DefaultWorld"] = new WorldRow { Name = "DefaultWorld" };

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
                _client = await InitializeSupabase();
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
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("-world="))
                    WorldName = arg.Substring(7);
            }

            Debug.Log($"[Server] Starting world: {WorldName}");

            // Start server
            NetworkManager.Singleton.StartServer();

            // Register to Supabase
            string serverIp = Environment.GetEnvironmentVariable("SERVER_IP") ?? Environment.GetEnvironmentVariable("IP");
            string serverPort = Environment.GetEnvironmentVariable("SERVER_PORT") ?? Environment.GetEnvironmentVariable("PORT");
            Debug.Log($"[Server] Registering {serverIp}:{serverPort} to Supabase for {WorldName}");
            await RegisterWorldToSupabase(WorldName, serverIp, ushort.Parse(serverPort));
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

    private async Task RegisterWorldToSupabase(string worldName, string ip, ushort port)
    {
        if (useLocalServer)
        {
            Debug.Log("[Server] Skipping Supabase registration when hosted locally.");
            return;
        }

        try
        {
            Debug.Log("[Server] Registering new world row to Supabase...");
            var world = new WorldRow
            {
                Name = worldName,
                Ip = ip,
                Port = port
                //createdAt = DateTime.UtcNow.ToString("o")
            };

            var response = await _client.From<WorldRow>().Insert(world);

            Debug.Log($"[Server] World '{worldName}' registered in Supabase at {ip}:{port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Server] Failed to register world in Supabase: {e}");
        }
    }

    private void OnJoinWorldRequest(ulong clientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string requestedWorld);

        Debug.Log($"[Server] Client {clientId} requested world: {requestedWorld}");

        if(!worlds.ContainsKey(requestedWorld))
        {
            Debug.LogWarning($"[Server] World '{requestedWorld}' does not exist. Creating it...");
            worlds[requestedWorld] = new WorldRow { Name = requestedWorld };
        }

        // Remove player from any existing world
        foreach (var w in worlds.Values)
            w.ConnectedPlayers.Remove(clientId);

        // Add to new world
        worlds[requestedWorld].ConnectedPlayers.Add(clientId);

        Debug.Log($"[Server] Player {clientId} joined {requestedWorld}");

        // Spawn the player object but parent or tag it to that world
        var player = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        player.GetComponent<Player>().CurrentWorld = requestedWorld; // store world name in Player
    }

    private async Task<Supabase.Client> InitializeSupabase()
    {
        // Search for supabase service key in launch parameters
        string[] args = Environment.GetCommandLineArgs();
        string supabaseKey = "";
        foreach (var arg in args)
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

        var client = new Supabase.Client(SUPABASE_URL, supabaseKey, clientOptions);
        await client.InitializeAsync();
        Debug.Log("[Server] Supabase initialized successfully.");
        return client;
    }

    [Table("worlds")]
    [Serializable]
    public class WorldRow : BaseModel
    {
        //[PrimaryKey("id", false)]
        //public long Id { get; set; }

        [Column("name")] public string Name { get; set; }
        [Column("ip")] public string Ip { get; set; }
        [Column("port")] public int Port { get; set; }

        // These fields ecist only in memory, not in Supabase
        [NonSerialized] public List<ulong> ConnectedPlayers = new(); // Use Netcode IDs
    }
}