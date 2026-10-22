using Godot;

namespace OceanDemo.Boat;

/// <summary>Chase camera for the boat: trails behind it, hold RMB to orbit, mouse wheel to zoom.</summary>
public partial class BoatCamera : Camera3D
{
    [Export] public NodePath TargetPath { get; set; }
    [Export] public float Distance { get; set; } = 55f;
    [Export] public float Pitch { get; set; } = -15f;      // degrees
    [Export] public float LookHeight { get; set; } = 10f;  // aim point above the boat's origin
    [Export] public float Follow { get; set; } = 4f;       // how quickly the camera swings in behind the boat
    [Export] public float Sensitivity { get; set; } = 0.15f;

    Node3D _target;
    float _yaw;      // world yaw of the camera, chases the boat's heading plus the orbit offset
    float _orbit;    // RMB orbit offset from straight behind
    bool _looking;

    public override void _Ready()
    {
        _target = GetNodeOrNull<Node3D>(TargetPath ?? new NodePath());
        if (_target != null) _yaw = _target.GlobalRotation.Y;
    }

    /// <summary>Jumps straight behind the boat (when switching to this camera).</summary>
    public void Snap()
    {
        if (_target == null) return;
        _orbit = 0f;
        _yaw = _target.GlobalRotation.Y;
        Place();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Current) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _looking = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) Distance = Mathf.Max(12f, Distance / 1.15f);
            else if (wheel.ButtonIndex == MouseButton.WheelDown) Distance = Mathf.Min(400f, Distance * 1.15f);
        }
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } && _looking)
        {
            _looking = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (e is InputEventMouseMotion motion && _looking)
        {
            _orbit -= Mathf.DegToRad(motion.Relative.X * Sensitivity);
            Pitch = Mathf.Clamp(Pitch - motion.Relative.Y * Sensitivity, -80f, 10f);
        }
    }

    public override void _Process(double delta)
    {
        if (_target == null || !Current) return;
        float goal = _target.GlobalRotation.Y + _orbit;
        _yaw = Mathf.LerpAngle(_yaw, goal, 1f - Mathf.Exp(-Follow * (float)delta));
        Place();
    }

    void Place()
    {
        var aim = _target.GlobalPosition + Vector3.Up * LookHeight;
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(Pitch), _yaw, 0f));
        var position = aim + basis.Z * Distance;
        position.Y = Mathf.Max(position.Y, 2f); // never under the sea surface
        GlobalTransform = new Transform3D(basis, position).LookingAt(aim, Vector3.Up);
    }
}
