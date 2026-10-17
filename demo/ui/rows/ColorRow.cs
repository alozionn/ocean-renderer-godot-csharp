using Godot;
using Ocean.Core.Parameters;
using Vector3N = System.Numerics.Vector3;

namespace OceanDemo.UI;

public partial class ColorRow : HBoxContainer, IParameterRow
{
    Parameter _p;
    ColorPickerButton _picker;

    public void Bind(Parameter p)
    {
        _p = p;
        GetNode<Label>("Label").Text = p.Label;
        _picker = GetNode<ColorPickerButton>("Picker");
        _picker.ColorChanged += c => _p.Set(new Vector3N(c.R, c.G, c.B));
        _p.Changed += Sync;
        Sync();
    }

    public override void _ExitTree()
    {
        if (_p != null) _p.Changed -= Sync;
    }

    void Sync()
    {
        var v = (Vector3N)_p.Value;
        _picker.Color = new Color(v.X, v.Y, v.Z);
    }
}
