using Godot;
using Ocean.Core.Parameters;

namespace OceanDemo.UI;

public partial class BoolRow : CheckBox, IParameterRow
{
    Parameter _p;

    public void Bind(Parameter p)
    {
        _p = p;
        Text = p.Label;
        Toggled += on => _p.Set(on);
        _p.Changed += Sync;
        Sync();
    }

    public override void _ExitTree()
    {
        if (_p != null) _p.Changed -= Sync;
    }

    void Sync() => SetPressedNoSignal((bool)_p.Value);
}
