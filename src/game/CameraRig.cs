using Godot;

namespace Hollowdeep.Game;

/// <summary>Orbit camera: arrows/edge-free pan, wheel zoom, MMB rotate. Presentation only.</summary>
public partial class CameraRig : Node3D
{
    private Camera3D _camera = null!;
    private Node3D _pivot = null!;
    private float _distance = 34f;
    private float _yaw = Mathf.Pi * 0.25f;
    private float _pitch = -0.95f;
    private bool _rotating;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _pivot = new Node3D();
        AddChild(_pivot);
        _camera = new Camera3D { Fov = 45 };
        _pivot.AddChild(_camera);
        Position = new Vector3(20, 6, 32); // over the spawn clearing
        Apply();
    }

    private void Apply()
    {
        _pivot.Rotation = new Vector3(0, _yaw, 0);
        _camera.Rotation = new Vector3(_pitch, 0, 0);
        _camera.Position = _camera.Transform.Basis.Z * _distance; // local +Z points backwards
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float panSpeed = _distance * 0.9f * dt;
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (Input.IsKeyPressed(Key.Up)) move.Z -= 1;
        if (Input.IsKeyPressed(Key.Down)) move.Z += 1;
        if (move != Vector3.Zero)
        {
            var basis = new Basis(Vector3.Up, _yaw);
            Position += basis * move.Normalized() * panSpeed;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed:
                _distance = Mathf.Clamp(_distance * 0.9f, 8f, 90f);
                Apply();
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed:
                _distance = Mathf.Clamp(_distance * 1.1f, 8f, 90f);
                Apply();
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Middle:
                _rotating = mb.Pressed;
                break;
            case InputEventMouseMotion mm when _rotating:
                _yaw -= mm.Relative.X * 0.008f;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.006f, -1.45f, -0.35f);
                Apply();
                break;
        }
    }

    public void FocusOn(Vector3 worldPos)
    {
        Position = new Vector3(worldPos.X, worldPos.Y, worldPos.Z);
    }
}
