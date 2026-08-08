#include <metal_stdlib>
using namespace metal;

struct VSIn { float3 pos [[attribute(0)]]; float2 uv [[attribute(1)]]; };
struct Uniforms
{
    float aspect;
    float px; float py; float pz;
    float rx; float ry; float rz;
    float sx; float sy; float sz;
    float selected;
    float camPX; float camPY; float camPZ;
    float camRX; float camRY; float camRZ;
    float camUX; float camUY; float camUZ;
    float camFX; float camFY; float camFZ;
    float projectionScale;
};
struct VSOut { float4 position [[position]]; float2 uv; float shade; float selected; };

vertex VSOut vs_main(VSIn in [[stage_in]], constant Uniforms& u [[buffer(1)]])
{
    const float d2r = 0.017453292519943295;
    float3 p = in.pos * float3(u.sx,u.sy,u.sz);
    float ax=u.rx*d2r, ay=u.ry*d2r, az=u.rz*d2r;
    float sx=sin(ax), cx=cos(ax), sy=sin(ay), cy=cos(ay), sz=sin(az), cz=cos(az);
    p=float3(p.x, cx*p.y-sx*p.z, sx*p.y+cx*p.z);
    p=float3(cy*p.x+sy*p.z, p.y, -sy*p.x+cy*p.z);
    p=float3(cz*p.x-sz*p.y, sz*p.x+cz*p.y, p.z);
    p += float3(u.px,u.py,u.pz);

    float3 rel = p - float3(u.camPX,u.camPY,u.camPZ);
    float viewX = dot(rel,float3(u.camRX,u.camRY,u.camRZ));
    float viewY = dot(rel,float3(u.camUX,u.camUY,u.camUZ));
    float viewZ = dot(rel,float3(u.camFX,u.camFY,u.camFZ));
    float safeAspect=max(u.aspect,0.01);
    float f=max(u.projectionScale,0.01);

    VSOut o;
    o.position=float4(viewX*f/safeAspect, viewY*f, viewZ-0.15, viewZ);
    o.uv=in.uv;
    o.shade=clamp(1.08-p.z*0.035,0.74,1.0);
    o.selected=u.selected;
    return o;
}

fragment float4 ps_main(VSOut in [[stage_in]], texture2d<float> logo [[texture(0)]], sampler samp [[sampler(0)]])
{
    float3 tex = logo.sample(samp,in.uv).rgb;
    float lum = dot(tex,float3(0.299,0.587,0.114));
    float ghost = (lum - 0.5) * 0.18;
    float3 base = float3(0.70,0.71,0.73) + ghost;
    base *= in.shade;
    if(in.selected > 0.5) base = mix(base,float3(0.90,0.63,0.22),0.22);
    return float4(base,1.0);
}
