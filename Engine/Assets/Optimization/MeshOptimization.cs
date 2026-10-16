using System;
using System.Runtime.InteropServices;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Optimization;

/// <summary>
/// Optional post-import index ordering for the GPU vertex cache.
/// Geometry, vertex attributes, material slots and triangle winding are unchanged.
/// </summary>
public static class MeshOptimization
{
    public static bool TryOptimizeVertexCache(MeshAsset source, out MeshAsset optimized)
    {
        optimized = source;
        var indices = source.Indices;
        if (indices is null || indices.Length == 0 || indices.Length % 3 != 0 ||
            source.Positions.Length % 3 != 0)
            return false;

        var vertexCount = source.Positions.Length / 3;
        if (vertexCount == 0 || Array.Exists(indices, index => index >= (uint)vertexCount))
            return false;

        var ranges = source.Submeshes;
        var nextStart = 0;
        foreach (var range in ranges)
        {
            if (range.FirstIndex != nextStart || range.IndexCount < 0 ||
                range.IndexCount % 3 != 0 ||
                range.IndexCount > indices.Length - nextStart)
                return false;
            nextStart += range.IndexCount;
        }
        if (ranges.Length > 0 && nextStart != indices.Length)
            return false;

        if (!MeshOptimizerNative.IsNativeLibraryAvailable())
            return false;

        var reordered = new uint[indices.Length];
        var inputHandle = GCHandle.Alloc(indices, GCHandleType.Pinned);
        var outputHandle = GCHandle.Alloc(reordered, GCHandleType.Pinned);
        try
        {
            var input = inputHandle.AddrOfPinnedObject();
            var output = outputHandle.AddrOfPinnedObject();
            if (ranges.Length == 0)
            {
                MeshOptimizerNative.OptimizeVertexCache(output, input,
                    (nuint)indices.Length, (nuint)vertexCount);
            }
            else
            {
                foreach (var range in ranges)
                {
                    var offset = range.FirstIndex * sizeof(uint);
                    MeshOptimizerNative.OptimizeVertexCache(
                        IntPtr.Add(output, offset), IntPtr.Add(input, offset),
                        (nuint)range.IndexCount, (nuint)vertexCount);
                }
            }
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        finally
        {
            outputHandle.Free();
            inputHandle.Free();
        }

        optimized = source with { Indices = reordered };
        return true;
    }
}
