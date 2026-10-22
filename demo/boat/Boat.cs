using Godot;

namespace OceanDemo.Boat;

/// <summary>
/// A levitating boat: W/S throttle, A/D rudder, Shift for more speed. Holds a fixed height; no physics and no
/// coupling to the water. Only steers while <see cref="Controlled"/>.
/// </summary>
public partial class Boat : Node3D
{
    [Export] public float MaxSpeed { get; set; } = 14f;     // m/s
    [Export] public float Acceleration { get; set; } = 4f;  // m/s²
    [Export] public float Drag { get; set; } = 0.35f;       // speed lost per second when coasting, as a fraction
    [Export] public float TurnRate { get; set; } = 25f;     // degrees per second at full speed
    [Export] public float BoostFactor { get; set; } = 3f;

    public bool Controlled { get; set; }
    public float Speed { get; private set; }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
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
        RotateY(Mathf.DegToRad(TurnRate * rudder * steer * dt));
        GlobalPosition += -GlobalBasis.Z * Speed * dt;
    }
}
