using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AutoScrollText : MonoBehaviour
{
    [Tooltip("Vitesse de défilement en pixels/seconde")]
    public float scrollSpeed = 30f;

    private RectTransform textRect;
    private RectTransform maskRect;
    private float startX;
    private float maxOffset;
    private bool isInitialized = false;

    /// <summary>
    /// Appeler Init() **après** avoir activé l’objet ou l’avoir instancié,
    /// pour que le Layout soit déjà « buildé » et que les largeurs soient correctes.
    /// </summary>
    public void Init()
    {
        textRect = GetComponent<RectTransform>();
        maskRect = transform.parent.GetComponent<RectTransform>();

        if (textRect == null || maskRect == null)
        {
            Debug.LogWarning($"[AutoScrollText] Init failed: missing RectTransform sur {gameObject.name}");
            return;
        }

        // Forcer la mise à jour du Canvas et du layout pour avoir des dimensions exactes
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(maskRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);

        startX = textRect.anchoredPosition.x;
        maxOffset = textRect.rect.width - maskRect.rect.width;
        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized || textRect == null || maxOffset <= 0f)
            return;

        Vector2 pos = textRect.anchoredPosition;
        pos.x -= scrollSpeed * Time.deltaTime;
        if (pos.x <= -maxOffset)
        {
            pos.x = startX;
        }
        textRect.anchoredPosition = pos;
    }
}
