using System;
using Godot;
using WaterSample = Ocean.WaterSample;
using OceanNode = Ocean.Ocean;

namespace OceanDemo.Boat;

/// <summary>
/// A boat that rides the swell: W/S throttle, A/D rudder, Shift for more speed. Samples the water under six points of
/// its hull, fits heave, pitch and roll to them and follows that pose critically damped. Kinematic: no rigid body.
/// Without <see cref="Water"/> it holds its height. Only steers while <see cref="Controlled"/>.
/// </summary>
public partial class Boat : Node3D
{
    [Export] public float MaxSpeed { get; set; } = 14f;     // m/s
    [Export] public float Acceleration { get; set; } = 4f;  // m/s²
    [Export] public float Drag { get; set; } = 0.35f;       // speed lost per second when coasting, as a fraction
    [Export] public float TurnRate { get; set; } = 25f;     // degrees per second at full speed
    [Export] public float BoostFactor { get; set; } = 3f;
    [Export] public float Draft { get; set; } = 0f;         // origin height above the fitted water surface (m)
    [Export] public float FloatFrequency { get; set; } = 3f; // rad/s: how firmly the hull follows the water

    public bool Controlled { get; set; }
    public float Speed { get; private set; }
    /// <summary>The ocean to float on.</summary>
    public OceanNode Water { get; set; }

    // Hull points in boat space (bow at −z): bow, stern, both sides forward and aft. The Kenney hull at x2.5 is
    // 12 m wide and 33 m long; the points sit just inside the waterline. Symmetric, so the plane fit separates.
    static readonly Vector2[] Hull = { new(0f, -14f), new(0f, 14f), new(-5f, -6f), new(5f, -6f), new(-5.5f, 6f), new(5.5f, 6f) };
    public static ReadOnlySpan<Vector2> HullPoints => Hull;
    readonly Vector3[] _points = new Vector3[Hull.Length];
    readonly WaterSample[] _water = new WaterSample[Hull.Length];
    float _yaw;
    Vector3 _pose, _poseRate; // heave (m), pitch, roll (rad), and their rates

    public override void _Ready()
    {
        _yaw = GlobalRotation.Y;
        _pose = new Vector3(GlobalPosition.Y, 0f, 0f);
    }

    /// <summary>Moves the boat (heading in degrees) and puts it straight onto the water, for tools.</summary>
    public void Place(Vector3 position, float headingDegrees)
    {
        _yaw = Mathf.DegToRad(headingDegrees);
        GlobalPosition = position;
        _poseRate = Vector3.Zero;
        _pose = Fit(Vector3.Zero, out _) ?? new Vector3(position.Y, 0f, 0f);
        Pose();
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        float throttle = 0f, rudder = 0f;
        if (Controlled)
        {
            if (Input.IsKeyPressed(Key.W)) throttle += 1f;
            if (Input.IsKeyPressed(Key.S)) throttle -= 0.5f;
            if (Input.IsKeyPressed(Key.A)) rudder += 1f;
            if (Input.IsKeyPressed(Key.D)) rudder -= 1f;
        }
        float max = MaxSpeed * (Controlled && Input.IsKeyPressed(Key.Shift) ? BoostFactor : 1f);

        if (throttle != 0f) Speed = Mathf.Clamp(Speed + throttle * Acceleration * dt, -0.3f * max, max);
        else Speed *= Mathf.Exp(-Drag * dt);
        if (throttle > 0f && Speed > max) Speed = max;

        // Rudder needs way on: steering scales with speed (and reverses going astern).
        float steer = Mathf.Clamp(Speed / MaxSpeed, -1f, 1f);
        _yaw += Mathf.DegToRad(TurnRate * rudder * steer * dt);
        var velocity = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw)) * Speed;
        GlobalPosition += velocity * dt;

        if (Fit(velocity, out var targetRate) is { } target)
        {
            // Critically damped, fed the water's own rate of change: no lag behind the swell, while the chop the sampler
            // leaves out and the 60 Hz simulation steps are smoothed away.
            float w = FloatFrequency;
            _poseRate += (w * w * (target - _pose) + 2f * w * (targetRate - _poseRate)) * dt;
            _pose += _poseRate * dt;
        }
        Pose();
    }

    void Pose()
    {
        var p = GlobalPosition;
        GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(_pose.Y, _yaw, _pose.Z)), new Vector3(p.X, _pose.X, p.Z));
    }

    /// <summary>
    /// Least-squares plane through the water under the hull points: heave, pitch and roll, and how fast they change
    /// for a boat moving at <paramref name="velocity"/>. Null without water.
    /// </summary>
    Vector3? Fit(Vector3 velocity, out Vector3 rate)
    {
        rate = Vector3.Zero;
        if (Water == null) return null;
        var right = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        var back = new Vector3(Mathf.Sin(_yaw), 0f, Mathf.Cos(_yaw));
        for (int i = 0; i < Hull.Length; i++) _points[i] = GlobalPosition + right * Hull[i].X + back * Hull[i].Y;
        Water.SampleWater(_points, _water);
        float h = 0f, hx = 0f, hz = 0f, r = 0f, rx = 0f, rz = 0f, xx = 0f, zz = 0f;
        for (int i = 0; i < Hull.Length; i++)
        {
            var w = _water[i];
            // Rate of the surface height under a point moving with the boat: the water rises at Velocity.y while
            // its slope slides past at the boat's speed relative to the water's.
            float sx = -w.Normal.X / w.Normal.Y, sz = -w.Normal.Z / w.Normal.Y;
            float rise = w.Velocity.Y + sx * (velocity.X - w.Velocity.X) + sz * (velocity.Z - w.Velocity.Z);
            float x = Hull[i].X, z = Hull[i].Y;
            h += w.Height;
            hx += w.Height * x;
            hz += w.Height * z;
            r += rise;
            rx += rise * x;
            rz += rise * z;
            xx += x * x;
            zz += z * z;
        }
        int n = Hull.Length;
        float slopeX = hx / xx, slopeZ = hz / zz; // height gained per metre to starboard and per metre aft
        rate = new Vector3(r / n, -(rz / zz) / (1f + slopeZ * slopeZ), (rx / xx) / (1f + slopeX * slopeX));
        return new Vector3(h / n + Draft, Mathf.Atan(-slopeZ), Mathf.Atan(slopeX));
    }
}
