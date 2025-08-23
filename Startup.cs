using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Startup : MonoBehaviour
{
    public string Client;
    public string Server;

    void Start()
    {
        if (System.Environment.GetCommandLineArgs().Any(arg=>arg == "-port") || System.Environment.GetCommandLineArgs().Any(arg => arg == "-launch-as-server"))
        {
            Debug.Log("Starting server");
            SceneManager.LoadScene(Server);
        }
        else
        {
            Debug.Log("Starting client");
            SceneManager.LoadScene(Client);
        }
    }
}
