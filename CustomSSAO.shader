// Shader: Custom SSAO Shader
Shader "Shaders/CustomSSAOShader"
{
    Properties
    {
        _Radius ("Radius", Float) = 0.5
        _NoiseTexture ("Noise Texture", 2D) = "white" {} // not used yet
        _Intensity ("Intensity", Float) = 1.0
        _Bias ("Bias", Float) = 0.0001
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma enable_d3d11_debug_symbols // enable debug symbol, for renderdoc
            #pragma multi_compile SSAO_SAMPLE_LOW_QUALITY SSAO_SAMPLE_MEDIUM_QUALITY SSAO_SAMPLE_HIGH_QUALITY
            
            // Include core shader library for Unity
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
             
            // Vertex Input Structure
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            // Vertex to Fragment Output Structure
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

            // Samplers for G-buffer and textures
            sampler2D _GBuffer0;  // Albedo (not used here, but available)
            sampler2D _GBuffer2;  // Normals (normal/smoothness)
            sampler2D _CameraDepthTexture;  // Depth texture
            sampler2D _NoiseTexture;  // Noise texture
            float4x4 _ViewProjection;

            // Adjustable SSAO parameters
            float _Radius;
            float _Intensity;
            float _Bias;
            float _MaxDepth;

            // Vertex Shader
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Function to generate random hemisphere direction based on normal and random values
            float3 RandomHemisphereDirection(float3 normal, float2 rand)
            {
                // Generate random point in unit hemisphere
                float phi = 2.0 * 3.14159265 * rand.x; // Azimuthal angle
                float cosTheta = rand.y;                // Cosine of polar angle
                float sinTheta = sqrt(1.0 - cosTheta * cosTheta); // Sine of polar angle

                // Random direction in tangent space (local space)
                float3 tangentSample = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);

                // Create Tangent Space (Normal, Tangent, Bitangent)
                float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                float3 tangent = normalize(cross(up, normal));
                float3 bitangent = cross(normal, tangent);

                // Convert tangentSample to world space using tbn
                return normalize(tangent * tangentSample.x + bitangent * tangentSample.y + normal * tangentSample.z);
            }

            // Hash function to generate pseudo-random values
            // input.uv
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Fragment Shader
            float4 frag(v2f input) : SV_Target
            { 
                // Retrieve depth and calculate world space position
                float depth = tex2D(_CameraDepthTexture, input.uv).r;
                float3 FragPos = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);

                // Sample normal from G-buffer
                float3 normal = normalize(tex2D(_GBuffer2, input.uv).rgb);

                // Sample random noise vector with dynamic noise scale
                float2 noiseScale = float2(_ScreenParams.x / 4.0, _ScreenParams.y / 4.0);
                float3 randomvec = normalize(tex2D(_NoiseTexture, input.uv * noiseScale).xyz);

                float occlusion = 0.0;
                
                // Early exit if fragment is part of the skybox
                float lineardepth = LinearEyeDepth(depth, _ZBufferParams);
                if(lineardepth > 20000)
                {
                    return 1;
                }

                // Determine sample count based on quality level
                int _SampleCount;
                #ifdef SSAO_SAMPLE_HIGH_QUALITY
                    _SampleCount = 64;
                #elif SSAO_SAMPLE_MEDIUM_QUALITY
                    _SampleCount = 32;
                #elif SSAO_SAMPLE_LOW_QUALITY
                    _SampleCount = 16;
                #endif

                // Accumulate occlusion samples
                for (int j = 1; j < _SampleCount + 1; j++)
                {
                    // Generate random direction vector
                    float3 randVec = RandomHemisphereDirection(normal, float2(Hash(input.uv.x / j), Hash(input.uv.y * j)));

                    // Get sample point world position
                    float3 samplePosWS = FragPos + randVec * _Radius;
                    
                    // Project sample point into clip space
                    float4 samplePosCS = mul(_ViewProjection, float4(samplePosWS, 1.0));
                    samplePosCS.xyz /= samplePosCS.w;
                    samplePosCS.xy = (samplePosCS.xy * 0.5) + 0.5;
                    
                    // Calculate UV and depth of the sample
                    float2 sample_uv = float2(samplePosCS.x, 1 - samplePosCS.y);
                    float sample_z = samplePosCS.z;
                    
                    float sampledepth = tex2D(_CameraDepthTexture, sample_uv).r;

                    // Convert depths to linear space
                    float linearSample_z = LinearEyeDepth(sample_z, _ZBufferParams);
                    float linearsampleDepth = LinearEyeDepth(sampledepth, _ZBufferParams);

                    // Accumulate occlusion based on depth comparison
                    if(linearSample_z >= linearsampleDepth + _MaxDepth)
                    {
                        continue;
                    }
                    occlusion += (linearSample_z >= (linearsampleDepth + _Bias) ? 1.0 : 0.0) * _Intensity;
                }

                // Normalize occlusion and return the final value
                return 1 - occlusion / _SampleCount;
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Diffuse"
}