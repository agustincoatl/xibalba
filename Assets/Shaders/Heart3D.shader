Shader "Xibalba/Heart3D"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.85, 0.04, 0.07, 1)
        _ShadeColor ("Shade Color", Color) = (0.22, 0.01, 0.03, 1)
        _RimColor ("Rim Color", Color) = (1, 0.45, 0.42, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.2
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.55
        _SpecStrength ("Specular Strength", Range(0, 2)) = 0.25
        _LightDir ("Light Direction (to light)", Vector) = (-0.4, 0.6, -0.7, 0)
        _Alpha ("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Heart3D"
            Tags { "LightMode" = "Universal2D" }

            Cull Back
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadeColor;
                half4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _SpecStrength;
                float4 _LightDir;
                float _Alpha;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float3 lightDirWS = normalize(_LightDir.xyz);

                float ndl = saturate(dot(normalWS, lightDirWS));
                float wrapped = ndl * 0.5 + 0.5;
                half3 color = lerp(_ShadeColor.rgb, _BaseColor.rgb, wrapped);

                float3 halfDir = normalize(lightDirWS + viewDirWS);
                float specular = pow(saturate(dot(normalWS, halfDir)), 32.0) * _SpecStrength;

                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength;

                color += specular + _RimColor.rgb * rim;

                return half4(color, _BaseColor.a * _Alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
