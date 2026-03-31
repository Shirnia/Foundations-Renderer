
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class PixelateMaterialSwitcher : MonoBehaviour
{
    [Tooltip("The material used when Pixelate Feature is OFF.")]
    public Material normalMaterial;
    
    [Tooltip("The material used when Pixelate Feature is ON.")]
    public Material pixelMaterial;

    private Renderer m_Renderer;
    private bool m_LastState = false;

    void Start()
    {
        m_Renderer = GetComponent<Renderer>();
        // Initialize state
        UpdateMaterial(CheckFeatureState());
    }

    void Update()
    {
        bool currentState = CheckFeatureState();
        if (currentState != m_LastState)
        {
            UpdateMaterial(currentState);
            m_LastState = currentState;
        }
    }

    private bool CheckFeatureState()
    {
        // Check the global shader variable we set in the feature
        return Shader.GetGlobalFloat("_PixelateEnabled") > 0.5f;
    }

    private void UpdateMaterial(bool isPixelateOn)
    {
        if (m_Renderer == null) return;

        if (isPixelateOn && pixelMaterial != null)
        {
            m_Renderer.material = pixelMaterial;
        }
        else if (!isPixelateOn && normalMaterial != null)
        {
            m_Renderer.material = normalMaterial;
        }
    }
}
