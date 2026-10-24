#nullable enable
using System;
using System.Numerics;

namespace Ocean.Core.Bodies;

/// <summary>
/// Something standing or floating in the water: a rock, a post, a pontoon, a hull. Its footprint at the waterline is a
/// rounded rectangle in world xz (a circle when length = beam = 2 × rounding), plus how high it stands, how deep it
/// reaches and how fast it moves. The ocean keeps one list of these (Ocean.AddBody) and every water effect reads it:
/// stationary bodies fold into the shore data (contact foam where crests hit them, breaking, calm lee), the same
/// path islands take.
/// </summary>
public sealed class WaterBody
{
    /// <summary>World xz of the footprint's centre.</summary>
    public Vector2 Position { get; set; }
    /// <summary>Rotation about +y in radians, as Godot's yaw: 0 puts the length along world +x, positive turns +x towards −z.</summary>
    public float Heading { get; set; }
    /// <summary>Footprint size along the heading (m).</summary>
    public float Length { get; set; } = 1f;
    /// <summary>Footprint size across the heading (m).</summary>
    public float Beam { get; set; } = 1f;
    /// <summary>Corner radius (m), up to half the shorter side: 0 a sharp box, Beam / 2 a capsule.</summary>
    public float Rounding { get; set; }
    /// <summary>Height of its top above sea level (m). Negative: a submerged rock or reef the waves break over.</summary>
    public float Top { get; set; } = 1f;
    /// <summary>
    /// How far below sea level it reaches (m). Infinity (the default): it stands on the sea bed and is ground to the
    /// water. Finite (a hull): where the water is deeper, waves keep running under it, so a moored boat still rides them.
    /// </summary>
    public float Draft { get; set; } = float.PositiveInfinity;
    /// <summary>Width (m) of the slope around its foot: the sea bed rises over it to the waterline (rock debris, a sand
    /// bank), so swell calms and breaks there. 0: sheer sides. Only for grounded bodies.</summary>
    public float Skirt { get; set; }
    /// <summary>How far from its waterline (m) it counts as the nearest shore (contact foam strip). 0: from its size (<see cref="EffectiveReach"/>).</summary>
    public float Reach { get; set; }
    /// <summary>How much it shelters the water downwind of it (0–1): swell and chop calm there.</summary>
    public float Lee { get; set; } = 0.6f;
    /// <summary>Lee length per metre of the width it shows the waves.</summary>
    public float LeeLength { get; set; } = 3f;
    /// <summary>World velocity in xz (m/s), for effects that follow moving bodies. Zero for stationary pieces.</summary>
    public Vector2 Velocity { get; set; }
    /// <summary>Stationary bodies fold into the shore data; moving ones (a boat under way) are left out of it.</summary>
    public bool Stationary { get; set; } = true;

    /// <summary>A round footprint: a rock, a post, a buoy.</summary>
    public static WaterBody Circle(Vector2 centre, float radius, float top = 1f) => new()
    {
        Position = centre, Length = 2f * radius, Beam = 2f * radius, Rounding = radius, Top = top,
    };

    /// <summary>A rectangular footprint (a pontoon, a pier block), optionally with rounded corners.</summary>
    public static WaterBody Box(Vector2 centre, float heading, float length, float beam, float top = 1f, float rounding = 0f) => new()
    {
        Position = centre, Heading = heading, Length = length, Beam = beam, Top = top, Rounding = rounding,
    };

    /// <summary>A floating hull: a capsule footprint with a finite draft, so the waves run under it.</summary>
    public static WaterBody Hull(Vector2 centre, float heading, float length, float beam, float draft, float top = 1.5f) => new()
    {
        Position = centre, Heading = heading, Length = length, Beam = beam, Rounding = 0.5f * beam, Draft = draft, Top = top,
        Lee = 0.4f,
    };

    /// <summary><see cref="Reach"/>, or from the size: a third of the shorter side plus the skirt, 0.6–24 m.</summary>
    public float EffectiveReach => Reach > 0f ? Reach : Math.Clamp(0.35f * MathF.Min(Length, Beam) + Skirt, 0.6f, 24f);

    /// <summary>Unit vector of the length axis in world xz.</summary>
    public Vector2 Axis => new(MathF.Cos(Heading), -MathF.Sin(Heading));

    /// <summary>Signed distance (m) from a world xz to the footprint's edge: negative inside.</summary>
    public float SignedDistance(Vector2 xz)
    {
        var axis = Axis;
        var d = xz - Position;
        float r = Math.Clamp(Rounding, 0f, 0.5f * MathF.Min(Length, Beam));
        float qx = MathF.Abs(Vector2.Dot(d, axis)) - (0.5f * Length - r);
        float qz = MathF.Abs(d.X * -axis.Y + d.Y * axis.X) - (0.5f * Beam - r);
        float outside = new Vector2(MathF.Max(qx, 0f), MathF.Max(qz, 0f)).Length();
        return outside + MathF.Min(MathF.Max(qx, qz), 0f) - r;
    }

    /// <summary>Half the footprint's extent along a world direction (unit vector).</summary>
    public float HalfExtent(Vector2 direction)
    {
        var axis = Axis;
        float r = Math.Clamp(Rounding, 0f, 0.5f * MathF.Min(Length, Beam));
        return (0.5f * Length - r) * MathF.Abs(Vector2.Dot(direction, axis))
            + (0.5f * Beam - r) * MathF.Abs(direction.X * -axis.Y + direction.Y * axis.X) + r;
    }

    /// <summary>A copy, so a fold can't change under a caller still editing the original.</summary>
    public WaterBody Clone() => (WaterBody)MemberwiseClone();
}
