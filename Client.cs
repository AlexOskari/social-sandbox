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
using Supabase;
using Supabase.Gotrue;
using static Supabase.Postgrest.Constants;
using UnityEngine.Networking;
using Newtonsoft.Json;
using System.Linq;

public class Client : MonoBehaviour
{
    public static Client Instance;

    [Header("Local Testing Settings")]
    public bool useLocalServer = false;        // Enable this for local testing
    public string localHost = "127.0.0.1";     // Usually localhost
    public ushort localPort = 7777;            // Port for local testing

    private UnityTransport transport;

    // Supabase settings
    const string SUPABASE_URL = "https://znfcfvrrsokbqymcbrsr.supabase.co";
    const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InpuZmNmdnJyc29rYnF5bWNicnNyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTg0NzA2MTgsImV4cCI6MjA3NDA0NjYxOH0.3NKjAHw43581tjQ06p8xlTUm9TZLdqf5IjTrZS1dxr8";
    public static Supabase.Client SupabaseClient {  get; private set; } 

    async void Awake()
    {
        if (Instance == null) Instance = this;

        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[Client] No UnityTransport found on NetworkManager!");
            return;
        }

        Debug.Log("[Client] Initializing Supabase...");
        await InitSupabase();

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
        catch (Exception e)
        {
            Debug.LogError($"[Client] Failed to initialize Unity Services: {e}");
        }
    }

    private async Task InitSupabase()
    {
        if (SupabaseClient != null) return;

        var options = new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = false
        };

        SupabaseClient = new Supabase.Client(SUPABASE_URL, SUPABASE_ANON_KEY, options);
        await SupabaseClient.InitializeAsync();

        if(SupabaseClient.Auth.CurrentSession == null)
        {
            // Default to guest/anonymous sign-in
            var session = await SupabaseClient.Auth.SignInAnonymously();
            if (session?.User != null)
                Debug.Log($"[Client] Signed in as anonymous user {session.User.Id}");
            else
                Debug.Log($"[Client] Anonymous sign-in failed");
        }
        else
        {
            Debug.Log($"[Client] Already signed in as {SupabaseClient.Auth.CurrentUser?.Id}");
        }
    }

    // Public helper for future email sign-up
    public async Task SignUpWithEmail(string email, string password)
    {
        var session = await SupabaseClient.Auth.SignUp(email, password);
        if (session?.User != null)
            Debug.Log($"[Client] Signed up {session.User.Email}");
        else
            Debug.LogError("[Client] Sign-up failed");
    }

    public async Task SignInWithEmail(string email, string password)
    {
        var session = await SupabaseClient.Auth.SignIn(email, password);
        if (session?.User != null)
            Debug.Log($"[Client] Signed in as {session.User.Email}");
        else
            Debug.Log("[Client] Sign-in failed");
    }

    public async Task<bool> UpgradeGuestToEmail(string email, string password)
    {
        var attrs = new UserAttributes { Email = email, Password = password };
        var updated = await SupabaseClient.Auth.Update(attrs);

        if (updated == null)
        {
            Debug.LogError("[Client] Failed to upgrade guest");
            return false;
        }

        Debug.Log($"[Client] Guest upgraded to {updated.Email}");
        return true;
    }

    public async Task JoinWorld(string worldName)
    {
        if (SupabaseClient == null)
        {
            Debug.LogWarning("[Client] Supabase not initialized yet — initializing now...");
            await InitSupabase();
        }

        Debug.Log($"[Client] Requesting to join world: {worldName}");

        if (useLocalServer)
        {
            // Local test: just connect directly
            Debug.Log($"Connecting to LOCAL server {localHost}:{localPort}");
            transport.SetConnectionData(localHost, localPort);
        }
        else
        {
            // Load world info from Supabase
            Debug.Log("[Client] Loading world info from Supabase...");

            try
            {
                // Single() returns Server.WorldRow (or null)
                var info = await SupabaseClient
                    .From<Server.WorldRow>()
                    .Filter("name", Operator.Equals, worldName) // or .Eq("name", worldName)
                    .Single();

                if(info == null)
                {
                    Debug.LogError($"[Client] World '{worldName}' not found in Supabase.");
                    return;
                }

                Debug.Log($"[Client] Connecting to {worldName} at {info.Ip}:{info.Port}");
                transport.SetConnectionData(info.Ip, (ushort)info.Port);
            }
            catch (Exception e)
            {
                Debug.Log($"[Client] Failed to load world from Supabase: {e.Message}");
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

    public void CreateOrJoinWorld(string worldId, string desiredWorldName = null)
    {
        var msg = new JoinWorldRequest { worldId = worldId, worldName = desiredWorldName };
        var json = JsonUtility.ToJson(msg);
        //SendMessageToServer("JoinWorld", json);
    }

    [Serializable]
    private class JoinWorldRequest
    {
        public string worldId;
        public string worldName;
    }

    // called when server replies "JoinWorldAccepted"
    /*private void OnJoinWorldAccepted(string json)
    {
        var data = JsonUtility.FromJson<JoinWorldAcceptedPayload>(json);
        Debug.Log($"Joined world: {data.worldName} (id: {data.worldId})");

        // load client-side scene/UI for the world, start syncing, etc.
    }*/
}