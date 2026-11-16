// The start street's shadows, drawn as geometry instead of read from a shadow map. A street model has
// this as its second material, so Unity draws its mesh a second time with it, every vertex pushed
// along the sun's direction down onto a flat plane just above the street's walkable surfaces:
//
//     t      = (planeY - world.y) / sun.y
//     shadow = world + sun * t
//
// The shadow is real geometry at screen resolution and moves with its model by fractions of a pixel,
// so it never shimmers: a shadow-map shadow of a moving model jumps a whole shadow-map texel at a time,
// which is what made the street's shadows flicker.
//
// It multiplies what is under it by the shadow tint. A closed mesh lands on the plane in several layers
// (its sun side and its far side, overlapping parts), and two models' shadows can overlap too; the
// stencil lets only the first layer through on each pixel, so the shadow is one even tone. Back faces
// of the flattened mesh are culled first, which already leaves one layer for a convex part.
//
// The plane sits just above the highest flat surface: the house plinths are 9 cm high and the houses are
// scaled up to 1.2, so 12 cm; then no flat surface covers it. A shadow on the road or the lawn floats a
// few centimetres over it, which this camera cannot tell. _DepthPull moves it a little towards the
// camera along its own view ray (the same pixel, a nearer depth) so it wins over the surfaces it lies on.
Shader "HopOn/PlanarShadow"
{
    Properties
    {
        _ShadowTint ("Shadow Tint (multiplies what is under it)", Color) = (0.22, 0.36, 0.48, 1)
        _SunDirection ("Sun Direction (world, the way the light travels)", Vector) = (-0.25, -0.866, 0.433, 0)
        _PlaneHeight ("Plane Height (world)", Float) = 0.12
        _DepthPull ("Depth Pull Towards Camera", Float) = 0.05
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Back keeps the sun side)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Name "PlanarShadow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]
            Blend DstColor Zero
            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask 1
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowTint;
                float4 _SunDirection;
                float _PlaneHeight;
                float _DepthPull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 sun = normalize(_SunDirection.xyz);
                world += sun * ((_PlaneHeight - world.y) / min(sun.y, -0.05));
                world += normalize(GetCameraPositionWS() - world) * _DepthPull;
                output.positionCS = TransformWorldToHClip(world);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _ShadowTint;
            }
            ENDHLSL
        }
    }
}
