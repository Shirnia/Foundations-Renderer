
using UnityEngine;

public class PixelateController : MonoBehaviour
{
    [Header("Settings")]
    public bool effectActive = true;
    [Range(64, 1080)]
    public int screenHeight = 144;
    public Color[] palette;

    void Update()
    {
        // Safety check to ensure the feature exists
        if (PixelateFeature.Instance == null) return;

        // 1. Toggle the entire feature ON/OFF
        if (PixelateFeature.Instance.isActive != effectActive)
        {
            PixelateFeature.Instance.SetActive(effectActive);
        }

        // 2. Update settings in real-time
        // Note: We modify the settings object directly.
        PixelateFeature.Instance.settings.screenHeight = screenHeight;
        
        // 3. Update the palette if needed
        if (palette != null && palette.Length > 0)
        {
            PixelateFeature.Instance.settings.palette = palette;
        }
    }

    // Example: Method to swap to a "Night Vision" palette via code
    public void SetNightVision()
    {
        palette = new Color[] { 
            Color.black, 
            new Color(0, 0.2f, 0), 
            new Color(0, 0.5f, 0), 
            new Color(0, 1f, 0) 
        };
        screenHeight = 120;
    }
}
