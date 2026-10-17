using System.Linq;
using Godot;
using Ocean.Core.Parameters;

namespace OceanDemo.UI;

public partial class EnumRow : HBoxContainer, IParameterRow
{
    Parameter _p;
    OptionButton _options;

    public void Bind(Parameter p)
    {
        _p = p;
        GetNode<Label>("Label").Text = p.Label;
        _options = GetNode<OptionButton>("Options");
        foreach (var o in p.Options) _options.AddItem(o);
        _options.ItemSelected += i => _p.Set(_p.Options[(int)i]);
        _p.Changed += Sync;
        Sync();
    }

    public override void _ExitTree()
    {
        if (_p != null) _p.Changed -= Sync;
    }

    void Sync() => _options.Select(_p.Options.ToList().IndexOf((string)_p.Value));
}
