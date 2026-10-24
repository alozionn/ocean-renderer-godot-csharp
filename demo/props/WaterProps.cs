using System.Collections.Generic;
using Godot;
using Ocean.Core.Bodies;
using OceanNode = Ocean.Ocean;

namespace OceanDemo.Props;

/// <summary>
/// Props standing in the water off the island's south coast: a sea stack and a floating pontoon held by four piles.
/// Each is a plain mesh plus a <see cref="WaterBody"/> describing its footprint; DemoRoot hands the bodies to the ocean,
/// which gives them the islands' shore treatment (foam where crests hit them, breaking on the rock's foot, lee).
/// </summary>
public partial class WaterProps : Node3D
{
    public List<WaterBody> Bodies { get; } = new();
    /// <summary>The water the pontoon rides on; null: it stays at sea level.</summary>
    public OceanNode Water { get; set; }

    MeshInstance3D _pontoon;

    public override void _Ready()
    {
        var rock = new StandardMaterial3D { AlbedoColor = new Color(0.33f, 0.3f, 0.27f), Roughness = 0.95f };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.25f, 0.16f), Roughness = 0.85f };
        var deck = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.45f, 0.33f), Roughness = 0.9f };

        // Sea stack: a stretched sphere 14 m across at the waterline, 15 m tall, debris sloping out 6 m around it.
        var stack = new Vector2(120f, -345f);
        Add(new SphereMesh { Radius = 7.2f, Height = 30f, RadialSegments = 32, Rings = 16 }, rock, new Vector3(stack.X, 0f, stack.Y), 0.4f);
        Bodies.Add(new WaterBody
        {
            Position = new(stack.X, stack.Y), Length = 14f, Beam = 14f, Rounding = 7f, Top = 15f, Skirt = 6f,
        });
        // A submerged boulder beside it, 1.5 m under the surface: the bigger crests break over it.
        var reef = new Vector2(104f, -330f);
        Add(new SphereMesh { Radius = 3f, Height = 4f, RadialSegments = 24, Rings = 8 }, rock, new Vector3(reef.X, -3.5f, reef.Y), 1.1f);
        Bodies.Add(new WaterBody { Position = new(reef.X, reef.Y), Length = 5f, Beam = 5f, Rounding = 2.5f, Top = -1.5f, Skirt = 3f });

        // Floating pontoon (12 x 2.4 m, 0.4 m freeboard, 0.5 m draft) and the four piles holding it.
        var pontoon = new Vector2(166f, -326f);
        float heading = Mathf.DegToRad(35f);
        var along = new Vector2(Mathf.Cos(heading), -Mathf.Sin(heading));
        var across = new Vector2(-along.Y, along.X);
        _pontoon = Add(new BoxMesh { Size = new Vector3(12f, 0.9f, 2.4f) }, deck, new Vector3(pontoon.X, -0.05f, pontoon.Y), heading);
        var float_ = WaterBody.Box(new(pontoon.X, pontoon.Y), heading, 12f, 2.4f, top: 0.4f, rounding: 0.2f);
        float_.Draft = 0.5f;
        Bodies.Add(float_);
        foreach (var (a, c) in new[] { (-5f, 1.6f), (5f, 1.6f), (-5f, -1.6f), (5f, -1.6f) })
        {
            var p = pontoon + along * a + across * c;
            Add(new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 9f }, wood, new Vector3(p.X, -2.5f, p.Y), 0f);
            Bodies.Add(WaterBody.Circle(new(p.X, p.Y), 0.15f, top: 2f));
        }
    }

    /// <summary>The pontoon rides the water between its piles (heave only).</summary>
    public override void _Process(double delta)
    {
        if (Water == null || _pontoon == null) return;
        var p = _pontoon.GlobalPosition;
        float target = Water.SampleWater(p).Height - 0.05f;
        p.Y = Mathf.Lerp(p.Y, target, 1f - Mathf.Exp(-4f * (float)delta));
        _pontoon.GlobalPosition = p;
    }

    MeshInstance3D Add(PrimitiveMesh mesh, Material material, Vector3 position, float yaw)
    {
        mesh.Material = material;
        var instance = new MeshInstance3D { Mesh = mesh, Position = position, Rotation = new Vector3(0f, yaw, 0f) };
        AddChild(instance);
        return instance;
    }
}
