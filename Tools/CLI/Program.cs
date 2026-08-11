using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Assets.Importing;
using TACTIX.Engine.Assets.Serialization;

// tactix import-obj <source.obj> <projectRoot> [assetProjectPath]

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintHelp();
        return 0;
    }

    try
    {
        return args[0] switch
        {
            "import-asset" => ImportAsset(args.Skip(1).ToArray()),
            "import-pending" => ImportPending(args.Skip(1).ToArray()),
            "import-obj" => ImportObj(args.Skip(1).ToArray()),
            _ => Fail($"Unknown command: {args[0]}")
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return 1;
    }
}

static void PrintHelp()
{
    Console.WriteLine("TACTIX CLI");
    Console.WriteLine("\nCommands:");
    Console.WriteLine("  import-asset <source.glb|source.gltf|source.blend> <projectRoot> [assetProjectPath]");
    Console.WriteLine("  import-pending <projectRoot>");
    Console.WriteLine("  import-obj <source.obj> <projectRoot> [assetProjectPath]");
}

static int Fail(string msg)
{
    Console.Error.WriteLine(msg);
    return 1;
}

static int ImportAsset(string[] args)
{
    if (args.Length is < 2 or > 3)
        return Fail("import-asset requires: <source.glb|source.gltf|source.blend> <projectRoot> [assetProjectPath]");

    var src = Path.GetFullPath(args[0]);
    var projectRoot = Path.GetFullPath(args[1]);
    if (!File.Exists(src)) return Fail($"File not found: {src}");

    var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (args.Length == 3)
        settings["assetProjectPath"] = args[2];

    var db = new AssetDatabase(projectRoot);
    db.Initialize();
    var result = AssetImportPipeline.CreateDefault().Import(db, new AssetImportRequest(src, "Assets/Models", settings));
    return PrintImportResult(result);
}

static int ImportPending(string[] args)
{
    if (args.Length != 1)
        return Fail("import-pending requires: <projectRoot>");

    var projectRoot = Path.GetFullPath(args[0]);
    var db = new AssetDatabase(projectRoot);
    db.Initialize();
    var results = AssetImportPipeline.CreateDefault().ImportPending(db, "Assets/Models");
    if (results.Count == 0)
    {
        Console.WriteLine("No pending imports.");
        return 0;
    }

    var failed = false;
    foreach (var result in results)
        failed |= PrintImportResult(result) != 0;
    return failed ? 1 : 0;
}

static int PrintImportResult(AssetImportResult result)
{
    if (!result.Success)
    {
        Console.Error.WriteLine(result.Message);
        return 1;
    }

    Console.WriteLine(result.Message);
    foreach (var asset in result.Assets)
        Console.WriteLine($"Registered asset: {asset.Guid} {asset.Type} {asset.ProjectPath}");
    return 0;
}

static int ImportObj(string[] args)
{
    if (args.Length is < 2 or > 3)
        return Fail("import-obj requires: <source.obj> <projectRoot> [assetProjectPath]");

    var src = Path.GetFullPath(args[0]);
    var projectRoot = Path.GetFullPath(args[1]);

    if (!File.Exists(src)) return Fail($"File not found: {src}");

    var imported = ObjImporter.Load(src);
    var mesh = new MeshAsset(
        AssetGuid.New(),
        imported.Positions3,
        null,
        null,
        imported.Indices.Select(i => checked((uint)i)).ToArray());

    var name = Path.GetFileNameWithoutExtension(src);
    var projectPath = args.Length == 3
        ? args[2]
        : $"Assets/Meshes/{name}.tasset";

    var db = new AssetDatabase(projectRoot);
    db.Initialize();
    var meta = db.SaveMesh(projectPath, mesh, name);

    Console.WriteLine($"Imported OBJ -> {meta.ProjectPath}");
    Console.WriteLine($"Registered asset: {meta.Guid}");
    return 0;
}

internal static class ObjImporter
{
    public static TactixMesh Load(string path)
    {
        var positions = new List<(float x, float y, float z)>();
        var faces = new List<(int a, int b, int c)>();

        foreach (var line in File.ReadLines(path))
        {
            var l = line.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;

            if (l.StartsWith("v "))
            {
                var parts = Split(l, 4);
                positions.Add((ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3])));
            }
            else if (l.StartsWith("f "))
            {
                // supports triangles and quads; ignores texcoord/normal.
                var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                var idx = parts.Skip(1).Select(ParseObjIndex).ToArray();
                if (idx.Length == 3)
                {
                    faces.Add((idx[0], idx[1], idx[2]));
                }
                else if (idx.Length == 4)
                {
                    faces.Add((idx[0], idx[1], idx[2]));
                    faces.Add((idx[0], idx[2], idx[3]));
                }
            }
        }

        if (positions.Count == 0 || faces.Count == 0)
            throw new InvalidDataException("OBJ had no vertices or faces.");

        var posOut = new float[positions.Count * 3];
        for (int i = 0; i < positions.Count; i++)
        {
            posOut[i * 3 + 0] = positions[i].x;
            posOut[i * 3 + 1] = positions[i].y;
            posOut[i * 3 + 2] = positions[i].z;
        }

        var idxOut = new int[faces.Count * 3];
        for (int i = 0; i < faces.Count; i++)
        {
            idxOut[i * 3 + 0] = faces[i].a;
            idxOut[i * 3 + 1] = faces[i].b;
            idxOut[i * 3 + 2] = faces[i].c;
        }

        return new TactixMesh { Positions3 = posOut, Indices = idxOut };
    }

    private static int ParseObjIndex(string token)
    {
        // token can be "v", "v/t", "v//n", "v/t/n"; OBJ indices are 1-based.
        var slash = token.IndexOf('/');
        var v = slash >= 0 ? token[..slash] : token;
        var idx = int.Parse(v);
        if (idx <= 0) throw new InvalidDataException("Only positive OBJ indices are supported.");
        return idx - 1;
    }

    private static string[] Split(string s, int expected)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expected) throw new InvalidDataException($"Malformed line: {s}");
        return parts;
    }

    private static float ParseF(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}
