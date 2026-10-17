using System.Collections.Generic;
using System.Linq;
using Godot;
using Ocean.Core.Parameters;

namespace OceanDemo.UI;

/// <summary>
/// Generic presenter: one foldable section per parameter set, a header per group, one row scene per parameter.
/// Knows nothing about what the parameters control. Toggled by a key; only in debug builds.
/// </summary>
public partial class ParameterPanel : PanelContainer
{
    [Export] public Key ToggleKey { get; set; } = Key.F1;
    [Export] public string Title { get; set; } = "";
    [Export] public PackedScene SectionScene { get; set; }
    [Export] public PackedScene GroupScene { get; set; }
    [Export] public PackedScene FloatRow { get; set; }
    [Export] public PackedScene IntRow { get; set; }
    [Export] public PackedScene BoolRow { get; set; }
    [Export] public PackedScene EnumRow { get; set; }
    [Export] public PackedScene ColorRow { get; set; }
    [Export] public PackedScene ActionRow { get; set; }
    [Export] public PackedScene ReadoutRow { get; set; }

    public override void _Ready()
    {
        if (!OS.IsDebugBuild())
        {
            QueueFree();
            return;
        }
        MouseFilter = MouseFilterEnum.Stop;
        GetNode<Label>("Box/Title").Text = $"{Title}  ({ToggleKey})";
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode == ToggleKey)
        {
            Visible = !Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    public void Show(IEnumerable<ParameterSet> sets)
    {
        var sections = GetNode<VBoxContainer>("Box/Scroll/Sections");
        foreach (var child in sections.GetChildren()) child.QueueFree();
        foreach (var set in sets)
        {
            var section = SectionScene.Instantiate<FoldableContainer>();
            section.Title = set.Title;
            sections.AddChild(section);
            var content = section.GetNode<VBoxContainer>("Content");
            foreach (var group in set.Parameters.OrderBy(p => p.Order).GroupBy(p => p.Group))
            {
                if (!string.IsNullOrEmpty(group.Key))
                {
                    var header = GroupScene.Instantiate<Label>();
                    header.Text = group.Key;
                    content.AddChild(header);
                }
                foreach (var p in group)
                {
                    var row = RowScene(p.Kind).Instantiate();
                    content.AddChild(row);
                    ((IParameterRow)row).Bind(p);
                }
            }
        }
    }

    PackedScene RowScene(ParameterKind kind) => kind switch
    {
        ParameterKind.Float => FloatRow,
        ParameterKind.Int => IntRow,
        ParameterKind.Bool => BoolRow,
        ParameterKind.Enum => EnumRow,
        ParameterKind.Color => ColorRow,
        ParameterKind.Action => ActionRow,
        _ => ReadoutRow,
    };
}
