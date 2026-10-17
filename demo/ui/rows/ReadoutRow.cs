using Godot;
using Ocean.Core.Parameters;

namespace OceanDemo.UI;

/// <summary>Read-only live value, polled a few times per second while visible.</summary>
public partial class ReadoutRow : HBoxContainer, IParameterRow
{
    const double Interval = 0.25;
    Parameter _p;
    Label _value;
    double _elapsed;

    public void Bind(Parameter p)
    {
        _p = p;
        GetNode<Label>("Label").Text = p.Label;
        _value = GetNode<Label>("Value");
        _value.Text = (string)p.Value;
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_p == null || _elapsed < Interval || !IsVisibleInTree()) return;
        _elapsed = 0;
        _value.Text = (string)_p.Value;
    }
}
