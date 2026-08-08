namespace TACTIX.Engine.Assets.Formats;

public sealed class TactixMesh
{
    public required float[] Positions3 { get; init; } // x,y,z per vertex
    public float[]? Colors4 { get; init; } // r,g,b,a per vertex
    public required int[] Indices { get; init; }
}
