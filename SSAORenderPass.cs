using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;


    public class CustomSSAOPass : ScriptableRenderPass
    {
        private static readonly int radiusId = Shader.PropertyToID("_Radius"); // Radius for sampling kernel in SSAO
        private static readonly int sampleCountId = Shader.PropertyToID("_SampleCount"); // Number of samples used in SSAO kernel
        private static readonly int intensityId = Shader.PropertyToID("_Intensity"); // Intensity of the SSAO effect

        private static readonly int horizontalBlurId = Shader.PropertyToID("_HorizontalBlur");
        private static readonly int verticalBlurId = Shader.PropertyToID("_VerticalBlur");

        private static readonly int biasId = Shader.PropertyToID("_Bias");
        private static readonly int maxDepthId = Shader.PropertyToID("_MaxDepth");


        private SSAOSettings ssaoSettings;
        private BlurSettings blurSettings;
        private Material ssaoMaterial;
        private Material blurMaterial;
        private Vector3[] sampleKernel;
        private Texture2D noiseTexture;

        private RenderTextureDescriptor ssaoTextureDescriptor;
        private RTHandle ssaoTextureHandle;

        private RTHandle blurTextureHandle; 
        private RenderTextureDescriptor blurTextureDescriptor; 

        public CustomSSAOPass(Material ssaoMaterial, Material blurMaterial, SSAOSettings ssaoSettings, BlurSettings blurSettings, Vector3[] sampleKernel, Texture2D noiseTexture)
        {
            this.ssaoMaterial = ssaoMaterial;
            this.blurMaterial = blurMaterial;
            this.ssaoSettings = ssaoSettings;
            this.blurSettings = blurSettings;
            this.sampleKernel = sampleKernel;
            this.noiseTexture = noiseTexture;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // Enable G-buffer rendering by modifying the camera texture descriptor.
            ssaoTextureDescriptor = cameraTextureDescriptor;

            //ssaoTextureDescriptor.colorFormat = RenderTextureFormat.R16;
            ssaoTextureDescriptor.colorFormat = RenderTextureFormat.ARGBFloat; // currently use ARGBFloat for learning purpose, adjust to store only red channal practically
            ssaoTextureDescriptor.depthBufferBits = 0;
            ssaoTextureDescriptor.useMipMap = false;
            ssaoTextureDescriptor.dimension = TextureDimension.Tex2D;
            ssaoTextureDescriptor.msaaSamples = 1;
            ssaoTextureDescriptor.width /= ssaoSettings.downsample;
            ssaoTextureDescriptor.height /= ssaoSettings.downsample;

            RenderingUtils.ReAllocateIfNeeded(ref ssaoTextureHandle, ssaoTextureDescriptor);

            // Set the blur texture size to be the same as the camera target size.
            blurTextureDescriptor = cameraTextureDescriptor;
            blurTextureDescriptor.width = cameraTextureDescriptor.width;
            blurTextureDescriptor.height = cameraTextureDescriptor.height;
            blurTextureDescriptor.colorFormat = RenderTextureFormat.ARGBFloat;
            blurTextureDescriptor.msaaSamples = 1;
            blurTextureDescriptor.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref blurTextureHandle, blurTextureDescriptor);
        }

        private void UpdateSSAOSettings()
        {
            if (ssaoMaterial == null) return;

            ssaoMaterial.SetFloat(radiusId, ssaoSettings.radius); // uniform float _Radius
            ssaoMaterial.SetFloat(intensityId, ssaoSettings.intensity); // uniform float _Intensity
            ssaoMaterial.SetFloat(biasId, ssaoSettings.bias); // uniform float _Bias
            ssaoMaterial.SetFloat(maxDepthId, ssaoSettings.maxDepth); // uniform float _MaxDepth

            if(ssaoSettings.quality == SampleQuality.LOW)
            {
                ssaoMaterial.EnableKeyword("SSAO_SAMPLE_LOW_QUALITY");
            }
            else if (ssaoSettings.quality == SampleQuality.MEDIUM)
            {
                ssaoMaterial.EnableKeyword("SSAO_SAMPLE_MEDIUM_QUALITY");
            }
            else if (ssaoSettings.quality == SampleQuality.HIGH)
            {
                ssaoMaterial.EnableKeyword("SSAO_SAMPLE_HIGH_QUALITY");
            }

            //for (int i = 0; i < ssaoSettings._SampleCount; i++)
            //{
            //    ssaoMaterial.SetVector("_Samples[" + i + "]", sampleKernel[i]); // uniform float3 _Samples[]
            //}
            //ssaoMaterial.SetTexture("_NoiseTexture", noiseTexture); // uniform Texture2D _NoiseTexture
        }

        private void UpdateBlurSettings()
        {
            if (blurMaterial == null) return;

            // Use the Volume settings or the default settings if no Volume is set.
            var volumeComponent =
                VolumeManager.instance.stack.GetComponent<CustomVolumeComponent>();
            float horizontalBlur = volumeComponent.horizontalBlur.overrideState ?
                volumeComponent.horizontalBlur.value : blurSettings.horizontalBlur;
            float verticalBlur = volumeComponent.verticalBlur.overrideState ?
                volumeComponent.verticalBlur.value : blurSettings.verticalBlur;

            blurMaterial.SetFloat(horizontalBlurId, horizontalBlur * 0.05f);
            blurMaterial.SetFloat(verticalBlurId, verticalBlur * 0.05f);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Custom SSAO Pass");

            renderingData.cameraData.requiresDepthTexture = true;
            CameraData cameraData = renderingData.cameraData;
            RTHandle cameraTargetHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RTHandle gbufferNormalRT = ((UniversalRenderer)renderingData.cameraData.renderer).GetGBufferRTHandle(2);
            RTHandle gbufferColorlRT = ((UniversalRenderer)renderingData.cameraData.renderer).GetGBufferRTHandle(0);
            
            UpdateSSAOSettings();
            UpdateBlurSettings();

            // view projection matrix
            cmd.SetGlobalMatrix("_ViewProjection", GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true) * cameraData.camera.worldToCameraMatrix);
            cmd.SetGlobalTexture("_CameraDepthTexture", renderingData.cameraData.renderer.cameraDepthTargetHandle); // depth texture
            cmd.SetGlobalTexture("_Gbuffer2", gbufferNormalRT); // gNormal

            cmd.Blit(null, ssaoTextureHandle, ssaoMaterial, 0);

            // TODO: better blur algorithm, now simply blur 3 times with x-axis and y-axis.
            Blit(cmd, ssaoTextureHandle, blurTextureHandle, blurMaterial, 0);
            Blit(cmd, blurTextureHandle, ssaoTextureHandle, blurMaterial, 1);

            cmd.SetGlobalTexture("_SSAO_Texture", ssaoTextureHandle);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(ssaoMaterial);
            CoreUtils.Destroy(blurMaterial);

            if (ssaoTextureHandle != null) ssaoTextureHandle.Release();
            if (blurTextureHandle != null) blurTextureHandle.Release();
        }
    }
