Shader "Custom/Triangle"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0, 0, 1)
        _CollisionColor ("Collision Color", Color) = (0, 1, 0, 1)
    }

    SubShader
    {
        Pass 
        {
            Name "Draw Triangle"
            Tags
            {
                "RenderType" = "Opaque"
            }
            LOD 200
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            #pragma target 5.0

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
            };

            float4x4 _LocalToWorldMatrix;
            StructuredBuffer<float3> _MeshVertices;
            
            StructuredBuffer<float4> _TriangleVertices;
            float4 _Color;
            float4 _CollisionColor;

            v2f vert(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
            {
                v2f o;
                float4 pos = _TriangleVertices[vertex_id + (instance_id * 6)];
                o.color = lerp(_Color, _CollisionColor, pos.w);
                o.position = UnityWorldToClipPos(mul(_LocalToWorldMatrix, float4(pos.xyz, 1.0)));
                //o.color = _Color;
                return o;
            }

            float4 frag(v2f i) : COLOR
            {
                return i.color;
            }
            ENDCG
        }
    }
    FallBack Off
}