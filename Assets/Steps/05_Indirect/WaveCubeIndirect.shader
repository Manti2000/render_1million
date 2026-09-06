// Rung 5 draw shader, the hand-written twin of Assets/Common/Shaders/WaveCube.shader. Nothing is
// uploaded per object: the vertex shader reads the object-to-world matrix the compute kernel wrote
// and hashes its own palette slot, so one Graphics.RenderMeshIndirect call draws the whole field.
// Shader Graph cannot index a StructuredBuffer by instance id, which is why this pass set is manual.
Shader "MillionObjects/WaveCubeIndirect"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "ObjectField.hlsl"

        // UnityIndirect.cginc only declares InitIndirectDrawArgs once it knows which argument layout
        // the draw uses. Graphics.RenderMeshIndirect with a mesh always draws indexed, so pick that
        // layout for the case where the compiler has not already chosen it.
        #ifndef UNITY_INDIRECT_DRAW_ARGS
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
        #endif
        #include "UnityIndirect.cginc"

        // Written by WaveCube.compute, one entry per object. Deliberately outside UnityPerMaterial:
        // an indirect draw is not SRP Batcher material, so there is no cbuffer to be compatible with.
        StructuredBuffer<float4x4> _LocalToWorld;
        StructuredBuffer<uint> _PaletteOverride;
        // Palette as a buffer rather than a float4[16] uniform: material array properties are dropped by
        // Unity at runtime, which rendered every cube black a few seconds after spawning.
        StructuredBuffer<float4> _Palette;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            uint svInstanceID : SV_InstanceID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            nointerpolation uint instanceID : TEXCOORD1;
        };

        // Shared by all three passes so depth, normals and colour agree on where a cube is.
        Varyings VertIndirect(Attributes input)
        {
            InitIndirectDrawArgs(0);
            uint instanceID = GetIndirectInstanceID(input.svInstanceID);
            float4x4 localToWorld = _LocalToWorld[instanceID];

            Varyings output;
            float3 positionWS = mul(localToWorld, float4(input.positionOS.xyz, 1.0)).xyz;
            output.positionCS = TransformWorldToHClip(positionWS);
            // The matrix carries a uniform scale, so normalizing the rotated normal is enough; no
            // inverse transpose is needed.
            output.normalWS = normalize(mul((float3x3)localToWorld, input.normalOS));
            output.instanceID = instanceID;
            return output;
        }

        // The recolour demo writes paletteIndex + 1 into _PaletteOverride; zero means "use the hash".
        half3 SampleAlbedo(uint instanceID)
        {
            uint slot = _PaletteOverride[instanceID];
            uint palette = slot != 0 ? slot - 1 : ObjectFieldPaletteIndex(instanceID);
            return (half3)_Palette[palette].rgb;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertIndirect
            #pragma fragment FragForward
            #include "../../Common/Shaders/WaveCubeLighting.hlsl"

            half4 FragForward(Varyings input) : SV_Target
            {
                return half4(ShadeCube(input.normalWS, SampleAlbedo(input.instanceID)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertIndirect
            #pragma fragment FragDepthOnly

            half FragDepthOnly(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertIndirect
            #pragma fragment FragDepthNormals

            half4 FragDepthNormals(Varyings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
