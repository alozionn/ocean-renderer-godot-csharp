using System;
using Godot;
using Ocean.Core.Parameters;

namespace OceanDemo.UI;

public partial class IntRow : HBoxContainer, IParameterRow
{
    Parameter _p;
    SpinBox _spin;

    public void Bind(Parameter p)
    {
        _p = p;
        GetNode<Label>("Label").Text = p.Label;
        _spin = GetNode<SpinBox>("Spin");
        (_spin.MinValue, _spin.MaxValue, _spin.Step) = (p.Min, p.Max, 1);
        _spin.ValueChanged += v => _p.Set((int)v);
        _p.Changed += Sync;
        Sync();
    }

    public override void _ExitTree()
    {
        if (_p != null) _p.Changed -= Sync;
    }

    void Sync() => _spin.SetValueNoSignal(Convert.ToInt32(_p.Value));
}
