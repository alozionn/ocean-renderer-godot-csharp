using Godot;
using Ocean.Core.Parameters;

namespace OceanDemo.UI;

public partial class ActionRow : Button, IParameterRow
{
    public void Bind(Parameter p)
    {
        Text = p.Label;
        Pressed += p.Invoke;
    }
}
