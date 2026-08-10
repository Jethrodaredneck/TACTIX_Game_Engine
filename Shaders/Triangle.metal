#include <metal_stdlib>
using namespace metal;

struct VSIn { float3 pos [[attribute(0)]]; float3 normal [[attribute(1)]]; float2 uv [[attribute(2)]]; };
struct Uniforms
{
    float aspect; float px; float py; float pz; float rx; float ry; float rz; float sx; float sy; float sz; float selected;
    float camPX; float camPY; float camPZ; float camRX; float camRY; float camRZ; float camUX; float camUY; float camUZ; float camFX; float camFY; float camFZ; float projectionScale;
    float baseR; float baseG; float baseB; float baseA; float useTexture;
    float dirDX; float dirDY; float dirDZ; float dirR; float dirG; float dirB; float dirIntensity; float ambient;
    float localType; float localPX; float localPY; float localPZ; float localDX; float localDY; float localDZ; float localR; float localG; float localB; float localIntensity; float localRange; float localInnerCos; float localOuterCos;
};
struct VSOut { float4 position [[position]]; float2 uv; float3 worldPosition; float3 worldNormal; float selected; };

static float3 rotate_euler(float3 v,float3 degrees)
{
    const float d2r=0.017453292519943295;float ax=degrees.x*d2r,ay=degrees.y*d2r,az=degrees.z*d2r;float sx=sin(ax),cx=cos(ax),sy=sin(ay),cy=cos(ay),sz=sin(az),cz=cos(az);v=float3(v.x,cx*v.y-sx*v.z,sx*v.y+cx*v.z);v=float3(cy*v.x+sy*v.z,v.y,-sy*v.x+cy*v.z);v=float3(cz*v.x-sz*v.y,sz*v.x+cz*v.y,v.z);return v;
}

vertex VSOut vs_main(VSIn in [[stage_in]],constant Uniforms& u [[buffer(1)]])
{
    float3 p=in.pos*float3(u.sx,u.sy,u.sz);p=rotate_euler(p,float3(u.rx,u.ry,u.rz));p+=float3(u.px,u.py,u.pz);float3 safeScale=max(abs(float3(u.sx,u.sy,u.sz)),float3(0.0001));float3 n=normalize(in.normal/safeScale);n=normalize(rotate_euler(n,float3(u.rx,u.ry,u.rz)));float3 rel=p-float3(u.camPX,u.camPY,u.camPZ);float viewX=dot(rel,float3(u.camRX,u.camRY,u.camRZ));float viewY=dot(rel,float3(u.camUX,u.camUY,u.camUZ));float viewZ=dot(rel,float3(u.camFX,u.camFY,u.camFZ));float safeAspect=max(u.aspect,0.01);float f=max(u.projectionScale,0.01);VSOut o;o.position=float4(viewX*f/safeAspect,viewY*f,viewZ-0.15,viewZ);o.uv=in.uv;o.worldPosition=p;o.worldNormal=n;o.selected=u.selected;return o;
}

fragment float4 ps_main(VSOut in [[stage_in]],texture2d<float> surfaceTexture [[texture(0)]],sampler samp [[sampler(0)]],constant Uniforms& u [[buffer(1)]])
{
    float3 sampled=surfaceTexture.sample(samp,in.uv).rgb;float3 base=float3(u.baseR,u.baseG,u.baseB);float3 albedo;
    if(u.useTexture>0.5) albedo=clamp(sampled*base,0.0,1.0);
    else { float lum=dot(sampled,float3(0.299,0.587,0.114));float ghost=(lum-0.5)*0.035;albedo=clamp(base+ghost,0.0,1.0); }
    float3 N=normalize(in.worldNormal);float3 dirL=normalize(-float3(u.dirDX,u.dirDY,u.dirDZ));float dirNdotL=max(dot(N,dirL),0.0);float3 lighting=float3(u.ambient)+dirNdotL*u.dirIntensity*float3(u.dirR,u.dirG,u.dirB);
    if(u.localType>0.5){float3 lightPos=float3(u.localPX,u.localPY,u.localPZ);float3 toLight=lightPos-in.worldPosition;float distance=max(length(toLight),0.0001);float3 L=toLight/distance;float ndotl=max(dot(N,L),0.0);float range=max(u.localRange,0.001);float rangeFactor=clamp(1.0-distance/range,0.0,1.0);float attenuation=rangeFactor*rangeFactor;if(u.localType>1.5){float3 fromLight=normalize(in.worldPosition-lightPos);float coneDot=dot(fromLight,normalize(float3(u.localDX,u.localDY,u.localDZ)));float cone=smoothstep(u.localOuterCos,u.localInnerCos,coneDot);attenuation*=cone;}lighting+=ndotl*attenuation*u.localIntensity*float3(u.localR,u.localG,u.localB);}
    float3 lit=min(albedo*lighting,float3(1.0));if(in.selected>0.5)lit=mix(lit,float3(0.95,0.62,0.18),0.22);return float4(lit,1.0);
}
