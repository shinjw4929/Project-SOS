using UnityEngine;

public class CursorController : MonoBehaviour
{
    [Header("Cursor Settings")]
    [SerializeField] private Texture2D cursorTexture;
    [SerializeField] private Vector2 hotspot;

    private void Awake()
    {
        ApplyCursor();
    }

    private void OnEnable()
    {
        ApplyCursor();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            ApplyCursor();
    }

    private void ApplyCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (cursorTexture == null)
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);
    }
}
