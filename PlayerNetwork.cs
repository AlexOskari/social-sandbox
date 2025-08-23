using Unity.Netcode;
using UnityEngine;

public class PlayerNetwork : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    private NetworkVariable<Vector2> networkPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Vector2 lastSentTile;

    private void Update()
    {
        if (IsOwner) HandleInput();
        else transform.position = Vector2.Lerp(transform.position, networkPos.Value, Time.deltaTime * 10f); // Interpolation
    }

    void HandleInput()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float y = Input.GetAxisRaw("Vertical");
        Vector2 inputDir = new(x,y);

        if(inputDir != Vector2.zero)
        {
            Vector2 newPos = (Vector2)transform.position + inputDir * moveSpeed * Time.deltaTime;
            transform.position = newPos;

            // Send update only if moved into a new tile
            Vector2 currentTile = new Vector2(Mathf.Round(newPos.x), Mathf.Round(newPos.y));
            if(currentTile != lastSentTile)
            {
                UpdatePositionServerRpc(currentTile);
                lastSentTile = currentTile;
            }
        }
    }

    [ServerRpc]
    void UpdatePositionServerRpc(Vector2 newTile)
    {
        // Server authorative position
        networkPos.Value = newTile;
    }
}
