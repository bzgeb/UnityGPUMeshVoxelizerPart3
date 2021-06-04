Shader "Custom/VoxelBlock"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard vertex:vert fullforwardshadows addshadow
        #pragma instancing_options procedural:setup

        // Use shader model 4.5 target to get compute shader support
        #pragma target 4.5

        struct Input
        {
            fixed4 color : COLOR;
        };

        fixed4 _Color;
        half _Clip;

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        StructuredBuffer<float4> _Positions;
        float4x4 _Matrix;
        #endif

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            float4 position = _Positions[unity_InstanceID];
            
            _Matrix = float4x4(
                1, 0, 0, position.x,
                0, 1, 0, position.y,
                0, 0, 1, position.z,
                0, 0, 0, 1
            );
            _Clip = -1.0 + position.w;
            #endif
        }

        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            v.vertex = mul(_Matrix, v.vertex);
            #endif
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            clip(_Clip);
            o.Albedo = _Color.rgb;
        }
        ENDCG
    }
    FallBack Off
}