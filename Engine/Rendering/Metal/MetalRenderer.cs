using CoreAnimation;
using CoreGraphics;
using Foundation;
using Metal;
using ImageIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using TACTIX.Engine.Core.Logging;
using TACTIX.Engine.Runtime.ECS;
using TACTIX.Editor.Scene;

namespace TACTIX.Engine.Rendering.Metal;

public sealed class MetalRenderer : IDisposable
{
    private readonly IMTLDevice _device;
    private readonly IMTLCommandQueue _queue;
    private readonly CAMetalLayer _layer;
    private readonly Dictionary<BuiltInMesh,(IMTLBuffer Buffer,int VertexCount)> _meshes=new();
    private IMTLLibrary _library;
    private IMTLRenderPipelineState _pipeline;
    private IMTLDepthStencilState _depthState;
    private IMTLBuffer _uniformBuffer;
    private IMTLTexture _logoTexture;
    private IMTLSamplerState _sampler;
    private World? _world;
    private EditorSelection? _selection;
    private int _drawCount;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Uniforms
    {
        public float Aspect;
        public float PX,PY,PZ;
        public float RX,RY,RZ;
        public float SX,SY,SZ;
        public float Selected;
        public float Padding;
    }

    public MetalRenderer(IMTLDevice device, CAMetalLayer layer, string shaderSource)
    {
        _device=device; _layer=layer; _queue=_device.CreateCommandQueue();
        _library=CompileLibrary(shaderSource); _pipeline=CreatePipeline(_library);
        _depthState=CreateDepthState();
        _uniformBuffer=_device.CreateBuffer((nuint)Marshal.SizeOf<Uniforms>(), MTLResourceOptions.CpuCacheModeDefault);
        _logoTexture=LoadLogoTexture(); _sampler=CreateSampler();
        BuildPrimitiveMeshes();
    }

    public void BindScene(World world, EditorSelection selection){_world=world;_selection=selection;}

    private IMTLLibrary CompileLibrary(string src){NSError? e;var l=_device.CreateLibrary(src,new MTLCompileOptions(),out e);if(e!=null)throw new InvalidOperationException(e.LocalizedDescription);return l;}

    private IMTLRenderPipelineState CreatePipeline(IMTLLibrary lib)
    {
        var d=new MTLRenderPipelineDescriptor{VertexFunction=lib.CreateFunction("vs_main"),FragmentFunction=lib.CreateFunction("ps_main")};
        d.ColorAttachments[0].PixelFormat=MTLPixelFormat.BGRA8Unorm; d.DepthAttachmentPixelFormat=MTLPixelFormat.Depth32Float;
        var vd=new MTLVertexDescriptor();
        vd.Attributes[0].Format=MTLVertexFormat.Float3;vd.Attributes[0].Offset=0;vd.Attributes[0].BufferIndex=0;
        vd.Attributes[1].Format=MTLVertexFormat.Float2;vd.Attributes[1].Offset=sizeof(float)*3;vd.Attributes[1].BufferIndex=0;
        vd.Layouts[0].Stride=sizeof(float)*5;vd.Layouts[0].StepFunction=MTLVertexStepFunction.PerVertex;d.VertexDescriptor=vd;
        NSError? e;var p=_device.CreateRenderPipelineState(d,out e);if(e!=null)throw new InvalidOperationException(e.LocalizedDescription);return p;
    }

    private IMTLDepthStencilState CreateDepthState()=>_device.CreateDepthStencilState(new MTLDepthStencilDescriptor{DepthCompareFunction=MTLCompareFunction.Less,DepthWriteEnabled=true});
    private IMTLSamplerState CreateSampler()=>_device.CreateSamplerState(new MTLSamplerDescriptor{MinFilter=MTLSamplerMinMagFilter.Linear,MagFilter=MTLSamplerMinMagFilter.Linear,SAddressMode=MTLSamplerAddressMode.Repeat,TAddressMode=MTLSamplerAddressMode.Repeat});

    private void BuildPrimitiveMeshes()
    {
        AddMesh(BuiltInMesh.Cube,Cube()); AddMesh(BuiltInMesh.Plane,Plane()); AddMesh(BuiltInMesh.Sphere,Sphere(20,12));
        AddMesh(BuiltInMesh.Cylinder,Cylinder(24)); AddMesh(BuiltInMesh.Cone,Cone(24)); AddMesh(BuiltInMesh.Capsule,Capsule(20,12));
    }
    private void AddMesh(BuiltInMesh type,float[] data)
    {
        var h=GCHandle.Alloc(data,GCHandleType.Pinned);try{var b=_device.CreateBuffer(h.AddrOfPinnedObject(),(nuint)(data.Length*sizeof(float)),MTLResourceOptions.CpuCacheModeDefault);_meshes[type]=(b,data.Length/5);}finally{h.Free();}
    }
    private static void V(List<float> a,float x,float y,float z,float u,float v){a.Add(x);a.Add(y);a.Add(z);a.Add(u);a.Add(v);}
    private static float[] Cube()=>[
        -1,-1, 1,0,1, 1, 1, 1,1,0, 1,-1, 1,1,1, -1,-1, 1,0,1,-1, 1, 1,0,0,1, 1, 1,1,0,
         1,-1,-1,0,1,-1, 1,-1,1,0,-1,-1,-1,1,1, 1,-1,-1,0,1, 1, 1,-1,0,0,-1, 1,-1,1,0,
        -1,-1,-1,0,1,-1, 1, 1,1,0,-1,-1, 1,1,1,-1,-1,-1,0,1,-1, 1,-1,0,0,-1, 1, 1,1,0,
         1,-1, 1,0,1, 1, 1,-1,1,0, 1,-1,-1,1,1, 1,-1, 1,0,1, 1, 1, 1,0,0, 1, 1,-1,1,0,
        -1, 1, 1,0,1,-1, 1,-1,0,0, 1, 1,-1,1,0,-1, 1, 1,0,1, 1, 1,-1,1,0, 1, 1, 1,1,1,
        -1,-1,-1,0,1, 1,-1, 1,1,0,-1,-1, 1,0,0,-1,-1,-1,0,1, 1,-1,-1,1,1, 1,-1, 1,1,0];
    private static float[] Plane()=>[-1,0,-1,0,0, 1,0,1,1,1, 1,0,-1,1,0, -1,0,-1,0,0,-1,0,1,0,1,1,0,1,1,1];

    private static float[] Sphere(int slices,int stacks)
    {
        var a=new List<float>();
        for(int y=0;y<stacks;y++)for(int x=0;x<slices;x++)
        {
            float u0=(float)x/slices,u1=(float)(x+1)/slices,v0=(float)y/stacks,v1=(float)(y+1)/stacks;
            void P(float u,float v){float th=u*MathF.PI*2,ph=(v-.5f)*MathF.PI;float cp=MathF.Cos(ph);V(a,cp*MathF.Sin(th),MathF.Sin(ph),cp*MathF.Cos(th),u*2,v*2);}
            P(u0,v0);P(u1,v1);P(u1,v0);P(u0,v0);P(u0,v1);P(u1,v1);
        }return a.ToArray();
    }
    private static float[] Cylinder(int n)
    {
        var a=new List<float>();for(int i=0;i<n;i++){float u0=(float)i/n,u1=(float)(i+1)/n,t0=u0*MathF.PI*2,t1=u1*MathF.PI*2;float x0=MathF.Sin(t0),z0=MathF.Cos(t0),x1=MathF.Sin(t1),z1=MathF.Cos(t1);
            V(a,x0,-1,z0,u0*3,1);V(a,x1,1,z1,u1*3,0);V(a,x1,-1,z1,u1*3,1);V(a,x0,-1,z0,u0*3,1);V(a,x0,1,z0,u0*3,0);V(a,x1,1,z1,u1*3,0);
            V(a,0,1,0,.5f,.5f);V(a,x1,1,z1,1,1);V(a,x0,1,z0,0,1);V(a,0,-1,0,.5f,.5f);V(a,x0,-1,z0,0,1);V(a,x1,-1,z1,1,1);}return a.ToArray();
    }
    private static float[] Cone(int n)
    {
        var a=new List<float>();for(int i=0;i<n;i++){float u0=(float)i/n,u1=(float)(i+1)/n,t0=u0*MathF.PI*2,t1=u1*MathF.PI*2;float x0=MathF.Sin(t0),z0=MathF.Cos(t0),x1=MathF.Sin(t1),z1=MathF.Cos(t1);
            V(a,0,1,0,.5f,0);V(a,x1,-1,z1,u1,1);V(a,x0,-1,z0,u0,1);V(a,0,-1,0,.5f,.5f);V(a,x0,-1,z0,0,1);V(a,x1,-1,z1,1,1);}return a.ToArray();
    }
    private static float[] Capsule(int slices,int stacks)
    {
        var a=new List<float>();
        for(int y=0;y<stacks;y++)for(int x=0;x<slices;x++)
        {float u0=(float)x/slices,u1=(float)(x+1)/slices,v0=(float)y/stacks,v1=(float)(y+1)/stacks;
            void P(float u,float v){float th=u*MathF.PI*2,ph=(v-.5f)*MathF.PI;float cp=MathF.Cos(ph);float yy=MathF.Sin(ph)+(v>.5f?.65f:-.65f);V(a,cp*MathF.Sin(th),yy,cp*MathF.Cos(th),u*2,v*2);}P(u0,v0);P(u1,v1);P(u1,v0);P(u0,v0);P(u0,v1);P(u1,v1);}return a.ToArray();
    }

    private IMTLTexture LoadLogoTexture()
    {
        string[] candidates={Path.Combine(NSBundle.MainBundle.ResourcePath??"","Assets","Branding","TACTIX_EngineLogo.jpg"),Path.Combine(AppContext.BaseDirectory,"Assets","Branding","TACTIX_EngineLogo.jpg"),Path.Combine(Directory.GetCurrentDirectory(),"Assets","Branding","TACTIX_EngineLogo.jpg")};
        string? path=null;foreach(var c in candidates)if(File.Exists(c)){path=c;break;}if(path==null)throw new FileNotFoundException("TACTIX primitive logo texture not found.");
        using var data=NSData.FromFile(path)??throw new InvalidOperationException("Failed to read TACTIX logo texture data.");using var source=CGImageSource.FromData(data)??throw new InvalidOperationException("Failed to decode TACTIX logo texture.");using var image=source.CreateImage(0,null)??throw new InvalidOperationException("Failed to create CGImage.");
        int w=(int)image.Width,h=(int)image.Height;var bytes=new byte[w*h*4];var handle=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{using var cs=CGColorSpace.CreateDeviceRGB();using(var ctx=new CGBitmapContext(handle.AddrOfPinnedObject(),w,h,8,w*4,cs,CGBitmapFlags.ByteOrder32Big|CGBitmapFlags.PremultipliedLast)){ctx.TranslateCTM(0,h);ctx.ScaleCTM(1,-1);ctx.DrawImage(new CGRect(0,0,w,h),image);}var td=MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.RGBA8Unorm,(nuint)w,(nuint)h,false);td.Usage=MTLTextureUsage.ShaderRead;var tex=_device.CreateTexture(td);tex.ReplaceRegion(new MTLRegion(new MTLOrigin(0,0,0),new MTLSize((nint)w,(nint)h,(nint)1)),0,handle.AddrOfPinnedObject(),(nuint)(w*4));return tex;}finally{handle.Free();}
    }

    public void Draw()
    {
        using var pool=new NSAutoreleasePool();_drawCount++;if(_drawCount==1)Log.Info("MetalRenderer.Draw: scene render path running");var drawable=_layer.NextDrawable();if(drawable==null)return;
        var tex=drawable.Texture;var depthDesc=MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.Depth32Float,tex.Width,tex.Height,false);depthDesc.Usage=MTLTextureUsage.RenderTarget;depthDesc.StorageMode=MTLStorageMode.Private;using var depth=_device.CreateTexture(depthDesc);
        var pass=new MTLRenderPassDescriptor();pass.ColorAttachments[0].Texture=tex;pass.ColorAttachments[0].LoadAction=MTLLoadAction.Clear;pass.ColorAttachments[0].StoreAction=MTLStoreAction.Store;pass.ColorAttachments[0].ClearColor=new MTLClearColor(.075,.078,.085,1);pass.DepthAttachment.Texture=depth;pass.DepthAttachment.LoadAction=MTLLoadAction.Clear;pass.DepthAttachment.StoreAction=MTLStoreAction.DontCare;pass.DepthAttachment.ClearDepth=1;
        var cmd=_queue.CommandBuffer();var enc=cmd.CreateRenderCommandEncoder(pass);enc.SetRenderPipelineState(_pipeline);enc.SetDepthStencilState(_depthState);enc.SetCullMode(MTLCullMode.Back);enc.SetFrontFacingWinding(MTLWinding.Clockwise);enc.SetFragmentTexture(_logoTexture,0);enc.SetFragmentSamplerState(_sampler,0);enc.SetViewport(new MTLViewport{OriginX=0,OriginY=0,Width=tex.Width,Height=tex.Height,ZNear=0,ZFar=1});
        if(_world!=null)
        {
            foreach(var (entity,mr) in _world.Query<MeshRendererComponent>())
            {
                if(!_world.Has<TransformComponent>(entity)||!_meshes.TryGetValue(mr.Mesh,out var mesh))continue;
                var t=_world.Get<TransformComponent>(entity);var u=new Uniforms{Aspect=(float)Math.Max(.01,(double)tex.Width/(double)Math.Max((nuint)1,tex.Height)),PX=t.Position.X,PY=t.Position.Y,PZ=t.Position.Z,RX=t.Rotation.X,RY=t.Rotation.Y,RZ=t.Rotation.Z,SX=t.Scale.X,SY=t.Scale.Y,SZ=t.Scale.Z,Selected=_selection?.ActiveEntity==entity?1:0};
                Marshal.StructureToPtr(u,_uniformBuffer.Contents,false);enc.SetVertexBuffer(mesh.Buffer,0,0);enc.SetVertexBuffer(_uniformBuffer,0,1);enc.DrawPrimitives(MTLPrimitiveType.Triangle,0,(nuint)mesh.VertexCount);
            }
        }
        enc.EndEncoding();cmd.PresentDrawable(drawable);cmd.Commit();
    }

    public void Dispose(){foreach(var m in _meshes.Values)m.Buffer.Dispose();_meshes.Clear();_logoTexture?.Dispose();_sampler?.Dispose();_uniformBuffer?.Dispose();_depthState?.Dispose();_pipeline?.Dispose();_library?.Dispose();_queue?.Dispose();}
}
