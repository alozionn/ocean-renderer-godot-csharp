using Godot;

namespace OceanDemo.Props;

/// <summary>
/// A seeded island heightmap, ported from Mushrook's GridData: domain-warped Perlin fbm, here pulled under the sea
/// towards the edges by a radial falloff so it reads as islands in open water. Heights are in "band units" (the
/// colour bands in island_terrain.gdshader); <see cref="HeightScale"/> turns them into metres.
/// </summary>
public partial class IslandTerrain : MeshInstance3D
{
    [Export] public int Seed { get; set; } = 7;
    [Export] public int Cells { get; set; } = 256;
    [Export] public float CellSize { get; set; } = 4f;
    [Export] public float MaxHeight { get; set; } = 15f;
    [Export] public float HeightScale { get; set; } = 4f;
    /// <summary>Normalised radius where the falloff starts sinking the land, and the band-unit depth it sinks to.</summary>
    [Export] public float FalloffStart { get; set; } = 0.45f;
    [Export] public float SeaFloor { get; set; } = -8f;

    /// <summary>Raised after every <see cref="Generate"/>, so the ocean can take the new sea floor.</summary>
    public event System.Action Generated;
    /// <summary>The last generated ground heights in world metres, (Cells + 1)² samples, row-major along z.</summary>
    public float[] WorldHeights { get; private set; } = System.Array.Empty<float>();
    public int Samples => Cells + 1;
    /// <summary>World xz of sample (0, 0). The node is only translated, never rotated or scaled.</summary>
    public Vector2 SampleOrigin => new Vector2(GlobalPosition.X, GlobalPosition.Z) - Vector2.One * (Cells * CellSize * 0.5f);

    public override void _Ready()
    {
        (MaterialOverride as ShaderMaterial)?.SetShaderParameter("caustic_texture", CausticTexture());
        Generate(Seed);
    }

    /// <summary>Mushrook's caustic pattern: ridged cellular noise. Seamless here, so the shader can simply repeat it.</summary>
    static ImageTexture CausticTexture()
    {
        var noise = new FastNoiseLite
        {
            Seed = 1,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = 0.015f,
            FractalType = FastNoiseLite.FractalTypeEnum.Ridged,
            FractalOctaves = 3,
            FractalLacunarity = 1f,
            FractalGain = 5f,
        };
        var image = noise.GetSeamlessImage(512, 512, false, false, 0.1f, false);
        image.Convert(Image.Format.L8);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    public void Reroll() => Generate((int)(GD.Randi() % 100000));

    public void Generate(int seed)
    {
        Seed = seed;
        var heights = HeightMap(seed);
        int n = Cells + 1;
        float half = Cells * CellSize * 0.5f;
        var vertices = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var world = new float[n * n];
        for (int row = 0; row < n; row++)
        for (int col = 0; col < n; col++)
        {
            float H(int r, int c) => heights[Mathf.Clamp(r, 0, Cells), Mathf.Clamp(c, 0, Cells)] * HeightScale;
            vertices[row * n + col] = new Vector3(col * CellSize - half, H(row, col), row * CellSize - half);
            world[row * n + col] = H(row, col) + GlobalPosition.Y;
            normals[row * n + col] = new Vector3(H(row, col - 1) - H(row, col + 1), 2f * CellSize, H(row - 1, col) - H(row + 1, col)).Normalized();
        }

        var indices = new int[Cells * Cells * 6];
        int i = 0;
        for (int row = 0; row < Cells; row++)
        for (int col = 0; col < Cells; col++)
        {
            int tl = row * n + col, tr = tl + 1, bl = tl + n, br = bl + 1;
            indices[i++] = tl; indices[i++] = tr; indices[i++] = br;
            indices[i++] = br; indices[i++] = bl; indices[i++] = tl;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        Mesh = mesh;
        (MaterialOverride as ShaderMaterial)?.SetShaderParameter("band_scale", HeightScale);
        WorldHeights = world;
        Generated?.Invoke();
    }

    float[,] HeightMap(int seed)
    {
        var noise = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.02f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
            FractalLacunarity = 2f,
            FractalGain = 0.5f,
        };
        var map = new float[Cells + 1, Cells + 1];
        for (int i = 0; i <= Cells; i++)
        for (int j = 0; j <= Cells; j++)
        {
            // Domain warping, as in Mushrook: two warp layers, then the final sample.
            float qx = noise.GetNoise2D(j, i);
            float qy = noise.GetNoise2D(j + 5.2f, i + 1.3f);
            float rx = noise.GetNoise2D(j + 4f * qx + 1.7f, i + 4f * qy + 9.2f);
            float ry = noise.GetNoise2D(j + 4f * qx + 8.3f, i + 4f * qy + 2.8f);
            float h = noise.GetNoise2D(j + 4f * rx, i + 4f * ry) * MaxHeight;

            float d = new Vector2(j, i).DistanceTo(new Vector2(Cells, Cells) * 0.5f) / (Cells * 0.5f);
            float land = 1f - Mathf.SmoothStep(FalloffStart, 1f, d);
            float height = Mathf.Lerp(SeaFloor, h + 2f, land); // +2 lifts the centre so there is land to fall off from
            // Shallow shelf: below sea level depth grows with its square, so the coast has clear shallows before it
            // drops off, still reaching SeaFloor at the edges.
            map[i, j] = height < 0f ? height * height / SeaFloor : height;
        }
        return map;
    }
}
