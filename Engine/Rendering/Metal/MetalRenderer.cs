using CoreAnimation;
using CoreGraphics;
using Foundation;
using ImageIO;
using Metal;
using System.Numerics;
using System.Runtime.InteropServices;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Core.Logging;
using TACTIX.Engine.Runtime.ECS;
using TACTIX.Engine.Runtime.Terrain;

namespace TACTIX.Engine.Rendering.Metal;

public sealed class MetalRenderer : IDisposable
{
    private sealed class TerrainGpu
    {
        public required IMTLBuffer Buffer;
        public required int VertexCount;
        public required string ContentHash;
    }

    private readonly IMTLDevice _device;
    private readonly IMTLCommandQueue _queue;
    private readonly CAMetalLayer _layer;
    private readonly Dictionary<BuiltInMesh, (IMTLBuffer Buffer, int VertexCount)> _meshes = new();
    private readonly Dictionary<AssetGuid, TerrainGpu> _terrainMeshes = new();
    private readonly IMTLLibrary _library;
    private readonly IMTLRenderPipelineState _pipeline;
    private readonly IMTLDepthStencilState _depthState;
    private readonly IMTLTexture _logoTexture;
    private readonly IMTLSamplerState _sampler;

    private World? _world;
    private AssetDatabase? _assets;
    private int _selectedEntityId;
    private Vector3 _cameraPosition = new(0, -0.97f, -6.93f);
    private Vector3 _cameraRight = Vector3.UnitX;
    private Vector3 _cameraUp = new(0, 0.99f, -0.14f);
    private Vector3 _cameraForward = new(0, 0.14f, 0.99f);
    private float _projectionScale = 1.7f;
    private int _drawCount;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Uniforms
    {
        public float Aspect;
        public float PX, PY, PZ;
        public float RX, RY, RZ;
        public float SX, SY, SZ;
        public float Selected;
        public float CamPX, CamPY, CamPZ;
        public float CamRX, CamRY, CamRZ;
        public float CamUX, CamUY, CamUZ;
        public float CamFX, CamFY, CamFZ;
        public float ProjectionScale;

        public float DirDX, DirDY, DirDZ;
        public float DirR, DirG, DirB;
        public float DirIntensity;
        public float Ambient;

        public float LocalType;
        public float LocalPX, LocalPY, LocalPZ;
        public float LocalDX, LocalDY, LocalDZ;
        public float LocalR, LocalG, LocalB;
        public float LocalIntensity;
        public float LocalRange;
        public float LocalInnerCos;
        public float LocalOuterCos;
    }

    private readonly record struct DirectionalLightData(Vector3 Direction, Vector3 Color, float Intensity);
    private readonly record struct LocalLightData(float Type, Vector3 Position, Vector3 Direction, Vector3 Color, float Intensity, float Range, float InnerCos, float OuterCos);

    public MetalRenderer(IMTLDevice device, CAMetalLayer layer, string shaderSource)
    {
        _device = device;
        _layer = layer;
        _queue = _device.CreateCommandQueue();
        _library = CompileLibrary(shaderSource);
        _pipeline = CreatePipeline(_library);
        _depthState = CreateDepthState();
        _logoTexture = LoadLogoTexture();
        _sampler = CreateSampler();
        BuildPrimitiveMeshes();
    }

    public void BindScene(World world) => _world = world;
    public void BindAssets(AssetDatabase assets) => _assets = assets;

    public void SetEditorView(Vector3 position, Vector3 right, Vector3 up, Vector3 forward, float projectionScale, Entity? selectedEntity)
    {
        _cameraPosition = position;
        _cameraRight = right;
        _cameraUp = up;
        _cameraForward = forward;
        _projectionScale = projectionScale;
        _selectedEntityId = selectedEntity?.Id ?? 0;
    }

    private IMTLLibrary CompileLibrary(string source)
    {
        NSError? error;
        var library = _device.CreateLibrary(source, new MTLCompileOptions(), out error);
        if (error != null) throw new InvalidOperationException(error.LocalizedDescription);
        return library;
    }

    private IMTLRenderPipelineState CreatePipeline(IMTLLibrary library)
    {
        var descriptor = new MTLRenderPipelineDescriptor
        {
            VertexFunction = library.CreateFunction("vs_main"),
            FragmentFunction = library.CreateFunction("ps_main")
        };
        descriptor.ColorAttachments[0].PixelFormat = MTLPixelFormat.BGRA8Unorm;
        descriptor.DepthAttachmentPixelFormat = MTLPixelFormat.Depth32Float;

        var vertex = new MTLVertexDescriptor();
        vertex.Attributes[0].Format = MTLVertexFormat.Float3;
        vertex.Attributes[0].Offset = 0;
        vertex.Attributes[0].BufferIndex = 0;
        vertex.Attributes[1].Format = MTLVertexFormat.Float3;
        vertex.Attributes[1].Offset = sizeof(float) * 3;
        vertex.Attributes[1].BufferIndex = 0;
        vertex.Attributes[2].Format = MTLVertexFormat.Float2;
        vertex.Attributes[2].Offset = sizeof(float) * 6;
        vertex.Attributes[2].BufferIndex = 0;
        vertex.Layouts[0].Stride = sizeof(float) * 8;
        vertex.Layouts[0].StepFunction = MTLVertexStepFunction.PerVertex;
        descriptor.VertexDescriptor = vertex;

        NSError? error;
        var pipeline = _device.CreateRenderPipelineState(descriptor, out error);
        if (error != null) throw new InvalidOperationException(error.LocalizedDescription);
        return pipeline;
    }

    private IMTLDepthStencilState CreateDepthState() =>
        _device.CreateDepthStencilState(new MTLDepthStencilDescriptor
        {
            DepthCompareFunction = MTLCompareFunction.Less,
            DepthWriteEnabled = true
        });

    private IMTLSamplerState CreateSampler() =>
        _device.CreateSamplerState(new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Linear,
            MagFilter = MTLSamplerMinMagFilter.Linear,
            SAddressMode = MTLSamplerAddressMode.Repeat,
            TAddressMode = MTLSamplerAddressMode.Repeat
        });

    private void BuildPrimitiveMeshes()
    {
        AddMesh(BuiltInMesh.Cube, Cube());
        AddMesh(BuiltInMesh.Plane, Plane());
        AddMesh(BuiltInMesh.Sphere, Sphere(24, 16));
        AddMesh(BuiltInMesh.Cylinder, Cylinder(28));
        AddMesh(BuiltInMesh.Cone, Cone(28));
        AddMesh(BuiltInMesh.Capsule, Capsule(24, 16));
    }

    private void AddMesh(BuiltInMesh type, float[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var buffer = _device.CreateBuffer(handle.AddrOfPinnedObject(), (nuint)(data.Length * sizeof(float)), MTLResourceOptions.CpuCacheModeDefault);
            _meshes[type] = (buffer, data.Length / 8);
        }
        finally { handle.Free(); }
    }

    private static void V(List<float> a, Vector3 p, Vector3 n, float u, float v)
    {
        a.Add(p.X); a.Add(p.Y); a.Add(p.Z);
        a.Add(n.X); a.Add(n.Y); a.Add(n.Z);
        a.Add(u); a.Add(v);
    }

    private static void Quad(List<float> a, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n)
    {
        V(a, p0, n, 0, 1); V(a, p1, n, 1, 0); V(a, p2, n, 1, 1);
        V(a, p0, n, 0, 1); V(a, p3, n, 0, 0); V(a, p1, n, 1, 0);
    }

    private static float[] Cube()
    {
        var a = new List<float>(36 * 8);
        Quad(a, new(-1,-1, 1), new( 1, 1, 1), new( 1,-1, 1), new(-1, 1, 1),  Vector3.UnitZ);
        Quad(a, new( 1,-1,-1), new(-1, 1,-1), new(-1,-1,-1), new( 1, 1,-1), -Vector3.UnitZ);
        Quad(a, new(-1,-1,-1), new(-1, 1, 1), new(-1,-1, 1), new(-1, 1,-1), -Vector3.UnitX);
        Quad(a, new( 1,-1, 1), new( 1, 1,-1), new( 1,-1,-1), new( 1, 1, 1),  Vector3.UnitX);
        Quad(a, new(-1, 1, 1), new( 1, 1,-1), new( 1, 1, 1), new(-1, 1,-1),  Vector3.UnitY);
        Quad(a, new(-1,-1,-1), new( 1,-1, 1), new(-1,-1, 1), new( 1,-1,-1), -Vector3.UnitY);
        return a.ToArray();
    }

    private static float[] Plane()
    {
        var a = new List<float>(6 * 8);
        Quad(a, new(-1,0,-1), new(1,0,1), new(1,0,-1), new(-1,0,1), Vector3.UnitY);
        return a.ToArray();
    }

    private static float[] Sphere(int slices, int stacks)
    {
        var a = new List<float>();
        Vector3 P(float u, float v)
        {
            var theta = u * MathF.PI * 2;
            var phi = (v - 0.5f) * MathF.PI;
            var cp = MathF.Cos(phi);
            return new(cp * MathF.Sin(theta), MathF.Sin(phi), cp * MathF.Cos(theta));
        }
        for (var y = 0; y < stacks; y++)
        for (var x = 0; x < slices; x++)
        {
            var u0=(float)x/slices; var u1=(float)(x+1)/slices;
            var v0=(float)y/stacks; var v1=(float)(y+1)/stacks;
            var p00=P(u0,v0); var p10=P(u1,v0); var p01=P(u0,v1); var p11=P(u1,v1);
            V(a,p00,p00,u0,v0); V(a,p11,p11,u1,v1); V(a,p10,p10,u1,v0);
            V(a,p00,p00,u0,v0); V(a,p01,p01,u0,v1); V(a,p11,p11,u1,v1);
        }
        return a.ToArray();
    }

    private static float[] Cylinder(int segments)
    {
        var a = new List<float>();
        for (var i=0;i<segments;i++)
        {
            var u0=(float)i/segments; var u1=(float)(i+1)/segments;
            var t0=u0*MathF.PI*2; var t1=u1*MathF.PI*2;
            var n0=new Vector3(MathF.Sin(t0),0,MathF.Cos(t0));
            var n1=new Vector3(MathF.Sin(t1),0,MathF.Cos(t1));
            var b0=new Vector3(n0.X,-1,n0.Z); var b1=new Vector3(n1.X,-1,n1.Z);
            var t0p=new Vector3(n0.X,1,n0.Z); var t1p=new Vector3(n1.X,1,n1.Z);
            V(a,b0,n0,u0,1); V(a,t1p,n1,u1,0); V(a,b1,n1,u1,1);
            V(a,b0,n0,u0,1); V(a,t0p,n0,u0,0); V(a,t1p,n1,u1,0);
            V(a,Vector3.UnitY,Vector3.UnitY,.5f,.5f); V(a,t1p,Vector3.UnitY,1,1); V(a,t0p,Vector3.UnitY,0,1);
            V(a,-Vector3.UnitY,-Vector3.UnitY,.5f,.5f); V(a,b0,-Vector3.UnitY,0,1); V(a,b1,-Vector3.UnitY,1,1);
        }
        return a.ToArray();
    }

    private static float[] Cone(int segments)
    {
        var a = new List<float>();
        var apex = new Vector3(0,1,0);
        for (var i=0;i<segments;i++)
        {
            var u0=(float)i/segments; var u1=(float)(i+1)/segments;
            var t0=u0*MathF.PI*2; var t1=u1*MathF.PI*2;
            var b0=new Vector3(MathF.Sin(t0),-1,MathF.Cos(t0));
            var b1=new Vector3(MathF.Sin(t1),-1,MathF.Cos(t1));
            var sideNormal=Vector3.Normalize(Vector3.Cross(b1-b0,apex-b0));
            V(a,apex,sideNormal,.5f,0); V(a,b1,sideNormal,u1,1); V(a,b0,sideNormal,u0,1);
            V(a,-Vector3.UnitY,-Vector3.UnitY,.5f,.5f); V(a,b0,-Vector3.UnitY,0,1); V(a,b1,-Vector3.UnitY,1,1);
        }
        return a.ToArray();
    }

    private static float[] Capsule(int slices, int stacks)
    {
        var a = new List<float>();
        Vector3 P(float u,float v,out Vector3 normal)
        {
            var theta=u*MathF.PI*2; var phi=(v-.5f)*MathF.PI;
            var cp=MathF.Cos(phi);
            normal=new Vector3(cp*MathF.Sin(theta),MathF.Sin(phi),cp*MathF.Cos(theta));
            var offset=v>.5f?.65f:-.65f;
            return new Vector3(normal.X,normal.Y+offset,normal.Z);
        }
        for(var y=0;y<stacks;y++) for(var x=0;x<slices;x++)
        {
            var u0=(float)x/slices; var u1=(float)(x+1)/slices; var v0=(float)y/stacks; var v1=(float)(y+1)/stacks;
            var p00=P(u0,v0,out var n00); var p10=P(u1,v0,out var n10); var p01=P(u0,v1,out var n01); var p11=P(u1,v1,out var n11);
            V(a,p00,n00,u0,v0); V(a,p11,n11,u1,v1); V(a,p10,n10,u1,v0);
            V(a,p00,n00,u0,v0); V(a,p01,n01,u0,v1); V(a,p11,n11,u1,v1);
        }
        return a.ToArray();
    }

    private IMTLTexture LoadLogoTexture()
    {
        string[] candidates =
        [
            Path.Combine(NSBundle.MainBundle.ResourcePath ?? "", "Assets", "Branding", "TACTIX_EngineLogo.jpg"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "TACTIX_EngineLogo.jpg"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Branding", "TACTIX_EngineLogo.jpg")
        ];
        var path = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("TACTIX primitive logo texture not found.");
        using var data = NSData.FromFile(path) ?? throw new InvalidOperationException("Failed to read TACTIX logo texture data.");
        using var source = CGImageSource.FromData(data) ?? throw new InvalidOperationException("Failed to decode TACTIX logo texture.");
        using var image = source.CreateImage(0, null) ?? throw new InvalidOperationException("Failed to create CGImage.");
        var w=(int)image.Width; var h=(int)image.Height; var bytes=new byte[w*h*4];
        var handle=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try
        {
            using var cs=CGColorSpace.CreateDeviceRGB();
            using(var ctx=new CGBitmapContext(handle.AddrOfPinnedObject(),w,h,8,w*4,cs,CGBitmapFlags.ByteOrder32Big|CGBitmapFlags.PremultipliedLast))
            {
                ctx.TranslateCTM(0,h); ctx.ScaleCTM(1,-1); ctx.DrawImage(new CGRect(0,0,w,h),image);
            }
            var td=MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.RGBA8Unorm,(nuint)w,(nuint)h,false);
            td.Usage=MTLTextureUsage.ShaderRead;
            var tex=_device.CreateTexture(td);
            tex.ReplaceRegion(new MTLRegion(new MTLOrigin(0,0,0),new MTLSize((nint)w,(nint)h,1)),0,handle.AddrOfPinnedObject(),(nuint)(w*4));
            return tex;
        }
        finally { handle.Free(); }
    }

    private DirectionalLightData ResolveDirectionalLight()
    {
        if (_world != null)
        {
            foreach (var (entity, light) in _world.Query<LightComponent>())
            {
                if (!light.Enabled || light.Type != LightType.Directional) continue;
                var rotation = _world.Has<TransformComponent>(entity) ? _world.Get<TransformComponent>(entity).Rotation : new Vector3(50,-30,0);
                return new DirectionalLightData(RotateEuler(Vector3.UnitZ, rotation), light.Color, MathF.Max(0, light.Intensity));
            }
        }
        return new DirectionalLightData(Vector3.Normalize(new Vector3(0.4f,-1f,0.3f)), new Vector3(1f,0.96f,0.88f), 1.15f);
    }

    private LocalLightData ResolveLocalLight()
    {
        if (_world != null)
        {
            foreach (var (entity, light) in _world.Query<LightComponent>())
            {
                if (!light.Enabled || light.Type == LightType.Directional) continue;
                var transform = _world.Has<TransformComponent>(entity) ? _world.Get<TransformComponent>(entity) : TransformComponent.Identity;
                var type = light.Type == LightType.Point ? 1f : 2f;
                var direction = RotateEuler(Vector3.UnitZ, transform.Rotation);
                var inner = MathF.Cos(Math.Clamp(light.InnerConeDegrees, 0f, 89.9f) * MathF.PI / 180f);
                var outer = MathF.Cos(Math.Clamp(MathF.Max(light.OuterConeDegrees, light.InnerConeDegrees + 0.01f), 0.01f, 89.99f) * MathF.PI / 180f);
                return new LocalLightData(type, transform.Position, direction, light.Color, MathF.Max(0, light.Intensity), MathF.Max(0.01f, light.Range), inner, outer);
            }
        }
        return new LocalLightData(0, Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 0, 1, 1, 0);
    }

    private static Vector3 RotateEuler(Vector3 vector, Vector3 degrees)
    {
        var r=MathF.PI/180f;
        var x=degrees.X*r; var y=degrees.Y*r; var z=degrees.Z*r;
        var sx=MathF.Sin(x); var cx=MathF.Cos(x); var sy=MathF.Sin(y); var cy=MathF.Cos(y); var sz=MathF.Sin(z); var cz=MathF.Cos(z);
        vector=new Vector3(vector.X,cx*vector.Y-sx*vector.Z,sx*vector.Y+cx*vector.Z);
        vector=new Vector3(cy*vector.X+sy*vector.Z,vector.Y,-sy*vector.X+cy*vector.Z);
        vector=new Vector3(cz*vector.X-sz*vector.Y,sz*vector.X+cz*vector.Y,vector.Z);
        return Vector3.Normalize(vector);
    }

    private TerrainGpu? GetTerrainGpu(AssetGuid guid)
    {
        if (_assets == null || !_assets.Registry.TryGet(guid, out var meta)) return null;
        if (_terrainMeshes.TryGetValue(guid, out var cached) && cached.ContentHash == meta.ContentHash) return cached;

        if (_terrainMeshes.Remove(guid, out var old)) old.Buffer.Dispose();

        var terrain = _assets.LoadTerrain(guid);
        var mesh = TerrainMeshGenerator.Generate(terrain);
        var expanded = new float[mesh.Indices.Length * 8];
        var dst = 0;
        foreach (var index in mesh.Indices)
        {
            var i = checked((int)index);
            var p=i*3; var uv=i*2;
            expanded[dst++]=mesh.Positions[p]; expanded[dst++]=mesh.Positions[p+1]; expanded[dst++]=mesh.Positions[p+2];
            expanded[dst++]=mesh.Normals[p]; expanded[dst++]=mesh.Normals[p+1]; expanded[dst++]=mesh.Normals[p+2];
            expanded[dst++]=mesh.UV0[uv]; expanded[dst++]=mesh.UV0[uv+1];
        }

        var handle=GCHandle.Alloc(expanded,GCHandleType.Pinned);
        try
        {
            var buffer=_device.CreateBuffer(handle.AddrOfPinnedObject(),(nuint)(expanded.Length*sizeof(float)),MTLResourceOptions.CpuCacheModeDefault);
            var gpu=new TerrainGpu{Buffer=buffer,VertexCount=expanded.Length/8,ContentHash=meta.ContentHash};
            _terrainMeshes[guid]=gpu;
            return gpu;
        }
        finally { handle.Free(); }
    }

    private Uniforms MakeUniforms(TransformComponent t, bool selected, float aspect, DirectionalLightData dir, LocalLightData local)
    {
        return new Uniforms
        {
            Aspect=aspect,
            PX=t.Position.X,PY=t.Position.Y,PZ=t.Position.Z,
            RX=t.Rotation.X,RY=t.Rotation.Y,RZ=t.Rotation.Z,
            SX=t.Scale.X,SY=t.Scale.Y,SZ=t.Scale.Z,
            Selected=selected?1:0,
            CamPX=_cameraPosition.X,CamPY=_cameraPosition.Y,CamPZ=_cameraPosition.Z,
            CamRX=_cameraRight.X,CamRY=_cameraRight.Y,CamRZ=_cameraRight.Z,
            CamUX=_cameraUp.X,CamUY=_cameraUp.Y,CamUZ=_cameraUp.Z,
            CamFX=_cameraForward.X,CamFY=_cameraForward.Y,CamFZ=_cameraForward.Z,
            ProjectionScale=_projectionScale,
            DirDX=dir.Direction.X,DirDY=dir.Direction.Y,DirDZ=dir.Direction.Z,
            DirR=dir.Color.X,DirG=dir.Color.Y,DirB=dir.Color.Z,DirIntensity=dir.Intensity,Ambient=.18f,
            LocalType=local.Type,
            LocalPX=local.Position.X,LocalPY=local.Position.Y,LocalPZ=local.Position.Z,
            LocalDX=local.Direction.X,LocalDY=local.Direction.Y,LocalDZ=local.Direction.Z,
            LocalR=local.Color.X,LocalG=local.Color.Y,LocalB=local.Color.Z,
            LocalIntensity=local.Intensity,LocalRange=local.Range,LocalInnerCos=local.InnerCos,LocalOuterCos=local.OuterCos
        };
    }

    private void DrawMesh(IMTLRenderCommandEncoder enc, IMTLBuffer meshBuffer, int vertexCount, Uniforms uniforms, List<IMTLBuffer> frameUniformBuffers)
    {
        var uniformBuffer=_device.CreateBuffer((nuint)Marshal.SizeOf<Uniforms>(),MTLResourceOptions.CpuCacheModeDefault);
        Marshal.StructureToPtr(uniforms,uniformBuffer.Contents,false);
        frameUniformBuffers.Add(uniformBuffer);
        enc.SetVertexBuffer(meshBuffer,0,0);
        enc.SetVertexBuffer(uniformBuffer,0,1);
        enc.SetFragmentBuffer(uniformBuffer,0,1);
        enc.DrawPrimitives(MTLPrimitiveType.Triangle,0,(nuint)vertexCount);
    }

    public void Draw()
    {
        using var pool=new NSAutoreleasePool();
        _drawCount++; if(_drawCount==1) Log.Info("MetalRenderer.Draw: lit primitives + terrain heightfield path running");
        var drawable=_layer.NextDrawable(); if(drawable==null) return;
        var tex=drawable.Texture;
        var depthDesc=MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.Depth32Float,tex.Width,tex.Height,false);
        depthDesc.Usage=MTLTextureUsage.RenderTarget; depthDesc.StorageMode=MTLStorageMode.Private;
        using var depth=_device.CreateTexture(depthDesc);
        var pass=new MTLRenderPassDescriptor();
        pass.ColorAttachments[0].Texture=tex; pass.ColorAttachments[0].LoadAction=MTLLoadAction.Clear; pass.ColorAttachments[0].StoreAction=MTLStoreAction.Store; pass.ColorAttachments[0].ClearColor=new MTLClearColor(.075,.078,.085,1);
        pass.DepthAttachment.Texture=depth; pass.DepthAttachment.LoadAction=MTLLoadAction.Clear; pass.DepthAttachment.StoreAction=MTLStoreAction.DontCare; pass.DepthAttachment.ClearDepth=1;

        var cmd=_queue.CommandBuffer();
        var enc=cmd.CreateRenderCommandEncoder(pass);
        enc.SetRenderPipelineState(_pipeline); enc.SetDepthStencilState(_depthState);
        enc.SetCullMode(MTLCullMode.Back); enc.SetFrontFacingWinding(MTLWinding.Clockwise);
        enc.SetFragmentTexture(_logoTexture,0); enc.SetFragmentSamplerState(_sampler,0);
        enc.SetViewport(new MTLViewport{OriginX=0,OriginY=0,Width=tex.Width,Height=tex.Height,ZNear=0,ZFar=1});

        var frameUniformBuffers=new List<IMTLBuffer>();
        var dir=ResolveDirectionalLight();
        var local=ResolveLocalLight();
        var aspect=(float)Math.Max(.01,(double)tex.Width/(double)Math.Max((nuint)1,tex.Height));

        if(_world!=null)
        {
            foreach(var (entity,mr) in _world.Query<MeshRendererComponent>())
            {
                if(!_world.Has<TransformComponent>(entity)||!_meshes.TryGetValue(mr.Mesh,out var mesh)) continue;
                var t=_world.Get<TransformComponent>(entity);
                DrawMesh(enc,mesh.Buffer,mesh.VertexCount,MakeUniforms(t,_selectedEntityId==entity.Id,aspect,dir,local),frameUniformBuffers);
            }

            foreach(var (entity,terrain) in _world.Query<TerrainComponent>())
            {
                if(!_world.Has<TransformComponent>(entity)) continue;
                var gpu=GetTerrainGpu(terrain.TerrainAssetGuid);
                if(gpu==null) continue;
                var t=_world.Get<TransformComponent>(entity);
                DrawMesh(enc,gpu.Buffer,gpu.VertexCount,MakeUniforms(t,_selectedEntityId==entity.Id,aspect,dir,local),frameUniformBuffers);
            }
        }

        enc.EndEncoding(); cmd.PresentDrawable(drawable);
        cmd.AddCompletedHandler(_=>{foreach(var buffer in frameUniformBuffers) buffer.Dispose();});
        cmd.Commit();
    }

    public void Dispose()
    {
        foreach(var mesh in _meshes.Values) mesh.Buffer.Dispose();
        _meshes.Clear();
        foreach(var terrain in _terrainMeshes.Values) terrain.Buffer.Dispose();
        _terrainMeshes.Clear();
        _logoTexture.Dispose(); _sampler.Dispose(); _depthState.Dispose(); _pipeline.Dispose(); _library.Dispose(); _queue.Dispose();
    }
}