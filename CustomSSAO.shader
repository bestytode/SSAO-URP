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

            // Function to generate random hemisphere direction based on normal and random values
            float3 RandomHemisphereDirection(float3 normal, float2 rand, float3x3 tbn)
            {
                // Generate random point in unit hemisphere
                float phi = 2.0 * 3.14159265 * rand.x; // Azimuthal angle
                float cosTheta = rand.y;                // Cosine of polar angle
                float sinTheta = sqrt(1.0 - cosTheta * cosTheta); // Sine of polar angle

                // Random direction in tangent space (local space)
                float3 tangentSample = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);

                // Create Tangent Space (Normal, Tangent, Bitangent)
                //float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                //float3 tangent = normalize(cross(up, normal));
                //float3 bitangent = cross(normal, tangent);

                // Convert tangentSample to world space using tbn
                return normalize(tbn[0] * tangentSample.x + tbn[1] * tangentSample.y + tbn[2] * tangentSample.z);
            }

            float3x3 GenerateTBNWithNoise(float3 normalWS, float3 noiseVector)
            {
                // Create an initial tangent that is not parallel to the normal.
                float3 tangent = abs(normalWS.y) > 0.999f ? float3(1, 0, 0) : float3(0, 1, 0);

                // Orthonormalize the tangent to make it perpendicular to the normal.
                tangent = normalize(tangent - dot(tangent, normalWS) * normalWS);

                // Generate the bitangent using a cross product between normal and tangent.
                float3 bitangent = cross(normalWS, tangent);

                // Perturb tangent and bitangent using the provided noise vector.
                tangent = normalize(tangent + noiseVector.r * 0.1);    // Use noiseVector.r for tangent perturbation.
                bitangent = normalize(bitangent + noiseVector.g * 0.1); // Use noiseVector.g for bitangent perturbation.

                // Re-orthogonalize to ensure TBN is valid.
                tangent = normalize(tangent - dot(tangent, normalWS) * normalWS);
                bitangent = cross(normalWS, tangent);

                // Construct the final TBN matrix.
                float3x3 tbnMatrix = float3x3(tangent, bitangent, normalWS);

                return tbnMatrix;
            }

            // Hash function to generate pseudo-random values
            // input.uv
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Vertex Shader
            v2f vert(appdata v)
            {
                v2f o;
                //o.pos = UnityObjectToClipPos(v.vertex);
                o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Fragment Shader
            float4 frag(v2f input) : SV_Target
            { 
                // Retrieve depth and calculate world space position
                // depth in screen space
                float depthSS = tex2D(_CameraDepthTexture, input.uv).r;
                //return depth;
                
                // reconstruct world space position
                float3 PosWS = ComputeWorldSpacePosition(input.uv, depthSS, UNITY_MATRIX_I_VP);
                //return float4(PosWS, 1);

                // Sample normal from G-buffer, normal in world space
                float3 normalWS = normalize(tex2D(_GBuffer2, input.uv).rgb);
                //return float4(normalWS, 1);

                // Sample random noise vector with dynamic noise scale
                float2 noiseScale = float2(_ScreenParams.x / 4.0, _ScreenParams.y / 4.0);
                float3 randomvec = normalize(tex2D(_NoiseTexture, input.uv * noiseScale).xyz * 2.0f - 1.0f);
                
                float occlusion = 0.0;
                
                // Early exit if fragment is part of the skybox
                float lineardepth = LinearEyeDepth(depthSS, _ZBufferParams);
                if(lineardepth > 10)
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

                //float3x3 tbn = GenerateTBNWithNoise(normalWS, randomvec);
                float3 tangent = normalize(randomvec - normalWS * dot(randomvec, normalWS));
                float3 bitangent = cross(normalWS, tangent);
                float3x3 TBN = float3x3(tangent, bitangent, normalWS);

                // Accumulate occlusion samples
                for (int j = 1; j < _SampleCount + 1; j++)
                {
                    // Generate random direction vector
                    float3 randVec = RandomHemisphereDirection(normalWS, float2(Hash(input.uv.x / j), Hash(input.uv.y * j)), TBN);

                    // Get sample point world position
                    float3 samplePosWS = PosWS + randVec * _Radius;
                    
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