using System.Text;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Serialization;

public static class AssetBinary
{
    private const uint MeshMagic = 0x48534D54; // 'TMSH'
    private const uint MatMagic  = 0x54414D54; // 'TMAT'

    public static void WriteMesh(string path, TactixMesh mesh)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);

        bw.Write(MeshMagic);
        bw.Write(1); // version

        bw.Write(mesh.Positions3.Length);
        foreach (var v in mesh.Positions3) bw.Write(v);

        bw.Write(mesh.Colors4?.Length ?? 0);
        if (mesh.Colors4 != null) foreach (var c in mesh.Colors4) bw.Write(c);

        bw.Write(mesh.Indices.Length);
        foreach (var i in mesh.Indices) bw.Write(i);
    }

    public static TactixMesh ReadMesh(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        var magic = br.ReadUInt32();
        if (magic != MeshMagic) throw new InvalidDataException("Not a TACTIX mesh.");

        var version = br.ReadInt32();
        if (version != 1) throw new InvalidDataException($"Unsupported mesh version {version}.");

        var posLen = br.ReadInt32();
        var pos = new float[posLen];
        for (int i = 0; i < posLen; i++) pos[i] = br.ReadSingle();

        var colLen = br.ReadInt32();
        float[]? cols = null;
        if (colLen > 0)
        {
            cols = new float[colLen];
            for (int i = 0; i < colLen; i++) cols[i] = br.ReadSingle();
        }

        var idxLen = br.ReadInt32();
        var idx = new int[idxLen];
        for (int i = 0; i < idxLen; i++) idx[i] = br.ReadInt32();

        return new TactixMesh { Positions3 = pos, Colors4 = cols, Indices = idx };
    }

    public static void WriteMaterial(string path, TactixMaterial mat)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);

        bw.Write(MatMagic);
        bw.Write(1); // version

        bw.Write(mat.Name);
        bw.Write(mat.BaseColorR);
        bw.Write(mat.BaseColorG);
        bw.Write(mat.BaseColorB);
        bw.Write(mat.BaseColorA);
    }

    public static TactixMaterial ReadMaterial(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        var magic = br.ReadUInt32();
        if (magic != MatMagic) throw new InvalidDataException("Not a TACTIX material.");

        var version = br.ReadInt32();
        if (version != 1) throw new InvalidDataException($"Unsupported material version {version}.");

        return new TactixMaterial
        {
            Name = br.ReadString(),
            BaseColorR = br.ReadSingle(),
            BaseColorG = br.ReadSingle(),
            BaseColorB = br.ReadSingle(),
            BaseColorA = br.ReadSingle()
        };
    }
}
