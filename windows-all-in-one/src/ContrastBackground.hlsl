// 循环背景「去背景 + 调色」—— WPF ShaderEffect 用的像素着色器（ps_2_0）
// build.ps1 用系统自带的 d3dcompiler_47.dll 把它编成 ContrastBackground.ps，作为资源嵌进 AIBootAnimations.exe。
//
// 只用于循环背景（开屏动画放完、透出应用界面之后），开屏动画本身始终原样播放。
// 输入是循环背景这一帧（预乘 alpha），输出也是预乘 alpha；整窗不透明度由 WPF 的 Opacity 再乘上去。
//
// 原版的循环背景是整段画面半透明盖上去。三段动画的背景都是暗色星空，盖在浅色界面上会压出一层灰，
// 亮的人物反而看不见；盖在深色界面上会提亮一层。这里在「原样」画面的基础上：
//   1. 去背景：按亮度把星空抠成透明（阈值每段动画单独定，见 Profile.KeyLo / KeyHi），人物和光效保留
//   2. 调色：在原色上加饱和、加对比；
//      浅色界面把中间调压深一点，白底上颜色立得住；深色界面把中间调提亮一点，黑底上更通透
//
// c0 Mix    0 = 原样（和原版完全一样），1 = 去背景 + 调色；从开屏过渡到循环背景时跟着整窗透明度一起渐变
// c1 Light  1 = 浅色界面，0 = 深色界面
// c2 KeyLo  亮度低于它算背景，完全透明
// c3 KeyHi  亮度高于它算人物，完全保留（两者之间平滑过渡）

sampler2D Input : register(s0);
float Mix : register(c0);
float Light : register(c1);
float KeyLo : register(c2);
float KeyHi : register(c3);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(Input, uv);
    float3 rgb = c.rgb / max(c.a, 0.0001);
    float3 W = float3(0.2126, 0.7152, 0.0722);
    float lum = dot(rgb, W);

    // 1. 去背景
    float m = saturate((lum - KeyLo) / max(KeyHi - KeyLo, 0.001));
    m = m * m * (3.0 - 2.0 * m);

    // 2. 调色：饱和度 ×1.4、对比度 ×1.2（以 0.5 为中心），再按界面深浅调中间调
    float3 g = saturate(lerp(lum.xxx, rgb, 1.4));
    g = saturate((g - 0.5) * 1.2 + 0.5);
    float3 col = lerp(pow(g, 0.8), pow(g, 1.5), Light);

    float a = m * c.a;
    return lerp(c, float4(col * a, a), Mix);
}
