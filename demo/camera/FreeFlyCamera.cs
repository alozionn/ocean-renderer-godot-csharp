using Godot;

namespace OceanDemo.Camera;

/// <summary>WASD, Q/E down/up, hold RMB to look, Shift x5, mouse wheel scales the speed by 1.2.</summary>
public partial class FreeFlyCamera : Camera3D
{
    [Export] public float Speed { get; set; } = 10f;
    [Export] public float Sensitivity { get; set; } = 0.15f;
    bool _looking;

    public override void _UnhandledInput(InputEvent e)
    {
        // Started here so clicks on the UI never start looking.
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _looking = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) Speed *= 1.2f;
            else if (wheel.ButtonIndex == MouseButton.WheelDown) Speed /= 1.2f;
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
            var r = Rotation;
            r.Y -= Mathf.DegToRad(motion.Relative.X * Sensitivity);
            r.X = Mathf.Clamp(r.X - Mathf.DegToRad(motion.Relative.Y * Sensitivity), -1.55f, 1.55f);
            r.Z = 0;
            Rotation = r;
        }
    }

    public override void _Process(double delta)
    {
        var dir = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) dir -= Basis.Z;
        if (Input.IsKeyPressed(Key.S)) dir += Basis.Z;
        if (Input.IsKeyPressed(Key.A)) dir -= Basis.X;
        if (Input.IsKeyPressed(Key.D)) dir += Basis.X;
        if (Input.IsKeyPressed(Key.E)) dir += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) dir -= Vector3.Up;
        if (dir == Vector3.Zero) return;
        float speed = Speed * (Input.IsKeyPressed(Key.Shift) ? 5f : 1f);
        GlobalPosition += dir.Normalized() * speed * (float)delta;
    }
}
