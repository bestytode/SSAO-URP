using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CustomSSAOFeature : ScriptableRendererFeature
{
    [SerializeField] private SSAOSettings ssaoSettings;
    [SerializeField] private BlurSettings blurSettings;
    [SerializeField] private Shader ssaoShader;
    [SerializeField] private Shader blurShader;

    private Material ssaoMaterial;
    private Material blurMaterial;
    private CustomSSAOPass ssaoPass;

    private Vector3[] sampleKernel; // not used yet
    private Texture2D noiseTexture; // not used yet

    public override void Create()
    {
        if (ssaoShader == null || blurShader == null)
        {
            Debug.LogError("CustomSSAOFeature: Shaders are not assigned.");
            return;
        }

        ssaoMaterial = CoreUtils.CreateEngineMaterial(ssaoShader);
        blurMaterial = CoreUtils.CreateEngineMaterial(blurShader);

        if (ssaoSettings == null)
        {
            ssaoSettings = new SSAOSettings();
            
        }
        if(blurSettings == null)
        {
            blurSettings = new BlurSettings();
        }

        //GenerateSampleKernel();
        GenerateNoiseTexture();

        ssaoPass = new CustomSSAOPass(ssaoMaterial, blurMaterial, ssaoSettings, blurSettings, sampleKernel, noiseTexture);
        ssaoPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ssaoMaterial == null || blurMaterial == null || ssaoPass == null)
        {
            Debug.LogWarning("CustomSSAOFeature: Materials or SSAOPass are not initialized.");
            return;
        }

        //if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(ssaoPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (ssaoPass != null)
        {
            ssaoPass.Dispose();
        }

        if (ssaoMaterial != null)
        {
            CoreUtils.Destroy(ssaoMaterial);
            CoreUtils.Destroy(blurMaterial);
        }
    }

    // Generate random sample kernel for ssao
    // not used yet :(
    /*private void GenerateSampleKernel()
    {
        // Improved SSAO Sample Kernel Generation Code
        sampleKernel = new Vector3[ssaoSettings.sampleCount];
        for (int i = 0; i < ssaoSettings.sampleCount; i++)
        {
            // Generate a random sample within the hemisphere
            Vector3 sample = new Vector3(
                UnityEngine.Random.value * 2.0f - 1.0f, // x value between -1 and 1
                UnityEngine.Random.value * 2.0f - 1.0f, // y value between -1 and 1
                UnityEngine.Random.value);              // z value between 0 and 1 (upper hemisphere)

            sample = sample.normalized;  // Normalize to get a unit vector

            // Scale by a random length, ensuring it is not too small (between 0.5 and 1.0)
            sample *= (0.5f + 0.5f * UnityEngine.Random.value);

            // Scale the samples so that they're more aligned near the origin
            float scale = (float)i / ssaoSettings.sampleCount;
            scale = Mathf.Lerp(0.1f, 1.0f, scale * scale);
            sample *= scale;

            // Ensure sample is not too small to avoid zero-length tangent issues
            if (sample.magnitude < 0.1f)
            {
                sample = sample.normalized * 0.1f; // Enforce a minimum length
            }

            // Optionally reduce alignment with the normal if needed
            float alignment = Vector3.Dot(sample.normalized, Vector3.forward); // Assuming normal points along z-axis
            if (Mathf.Abs(alignment) > 0.9f)
            {
                // Recompute or adjust sample to reduce alignment with the normal
                sample = new Vector3(sample.x, sample.y, 0.5f).normalized * sample.magnitude;
            }

            sampleKernel[i] = sample;
        }
    }*/

    // Generate a noise texture for SSAO
    // not used yet :(
    private void GenerateNoiseTexture()
    {
        int noiseSize = 4;
        Color[] noiseColors = new Color[noiseSize * noiseSize];
        for(int i = 0; i < noiseSize * noiseSize; i++)
        {
            Vector3 noise = new Vector3(
                UnityEngine.Random.value * 2.0f - 1.0f,
                UnityEngine.Random.value * 2.0f - 1.0f,
                0.0f); // Only randomizing X and Y directions for screen alignment.

            noiseColors[i] = new Color(noise.x, noise.y, noise.z);
        }

        this.noiseTexture = new Texture2D(noiseSize, noiseSize, TextureFormat.ARGB32, false);
        noiseTexture.SetPixels(noiseColors);
        noiseTexture.Apply();
    }
}

[Serializable]
public enum SampleQuality
{
    HIGH,
    MEDIUM,
    LOW
}

[Serializable]
public class SSAOSettings
{
    [Tooltip("The amount to downsample the SSAO pass. Lower values mean higher quality at a higher cost.")]
    [Range(1, 4)] public int downsample = 1;

    [Tooltip("The radius of the sampling area for SSAO. Larger values result in a wider area of influence.")]
    [Range(0.1f, 5.0f)] public float radius = 0.5f;

    [Tooltip("Quality of the SSAO sampling. Higher quality results in more sample points and better visual results.")]
    public SampleQuality quality = SampleQuality.HIGH;

    [Tooltip("The intensity of the ambient occlusion effect. A higher value makes the darkening effect more pronounced.")]
    [Range(0.5f, 2.0f)] public float intensity = 1.0f;

    [Tooltip("Bias used to reduce self-shadowing artifacts. Higher values can help prevent artifacts but may reduce effect accuracy.")]
    [Range(0, 1.0f)] public float bias = 1.0f;

    [Tooltip("Maximum depth that affects ambient occlusion. Controls the range in which SSAO samples.")]
    [Range(0, 10.0f)] public float maxDepth = 10.0f;
}


[Serializable]
public class BlurSettings
{
    [Range(0, 1.0f)] public float horizontalBlur;
    [Range(0, 1.0f)] public float verticalBlur;
}
