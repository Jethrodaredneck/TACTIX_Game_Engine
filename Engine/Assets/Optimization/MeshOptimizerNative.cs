using System;
using System.Runtime.InteropServices;

namespace TACTIX.Engine.Assets.Optimization;

/// <summary>
/// Optional meshoptimizer native bridge. This type is not invoked by the
/// default importer path. Native library packaging is validated separately.
/// </summary>
internal static partial class MeshOptimizerNative
{
    private const string LibraryName = "meshoptimizer";

    [DllImport(LibraryName, EntryPoint = "meshopt_generateVertexRemap", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint GenerateVertexRemap(
        IntPtr destination,
        IntPtr indices,
        nuint indexCount,
        IntPtr vertices,
        nuint vertexCount,
        nuint vertexSize);

    [DllImport(LibraryName, EntryPoint = "meshopt_remapIndexBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RemapIndexBuffer(
        IntPtr destination,
        IntPtr indices,
        nuint indexCount,
        IntPtr remap);

    [DllImport(LibraryName, EntryPoint = "meshopt_remapVertexBuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RemapVertexBuffer(
        IntPtr destination,
        IntPtr vertices,
        nuint vertexCount,
        nuint vertexSize,
        IntPtr remap);

    [DllImport(LibraryName, EntryPoint = "meshopt_optimizeVertexCache", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void OptimizeVertexCache(
        IntPtr destination,
        IntPtr indices,
        nuint indexCount,
        nuint vertexCount);

    internal static bool IsNativeLibraryAvailable()
    {
        if (!NativeLibrary.TryLoad(LibraryName, typeof(MeshOptimizerNative).Assembly, null, out var handle))
            return false;

        NativeLibrary.Free(handle);
        return true;
    }
}
