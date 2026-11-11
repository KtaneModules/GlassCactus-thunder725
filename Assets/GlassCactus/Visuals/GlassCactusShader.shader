Shader "KT/Transparent/Glass Cactus" {
	Properties {
		_Color ("Tint", Color) = (1,1,1,1)
		_Alpha ("Alpha", Range (0,1)) = 0.6 
	}
	SubShader {
		Tags {"Queue"="Transparent-200" "IgnoreProjector"="True" "RenderType"="Transparent"}
		LOD 150
		Cull Off // Two-Sided Material
	
		CGPROGRAM
		#pragma surface surf Lambert alpha
		
		fixed4 _Color;
		half _Alpha;
		
		struct Input {
			float null;
		};
		
		void surf (Input null, inout SurfaceOutput o) {
			o.Albedo = _Color.rgb;
			o.Alpha = _Alpha;
		}

		ENDCG
	}
	
	Fallback "Mobile/VertexLit"
}
