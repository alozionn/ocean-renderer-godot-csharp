using System;
using System.Globalization;
using Godot;
using Ocean.Core.Parameters;

namespace OceanDemo.UI;

/// <summary>Slider row; exponential parameters map the slider logarithmically. OnRelease parameters commit when a drag ends.</summary>
public partial class FloatRow : HBoxContainer, IParameterRow
{
    Parameter _p;
    HSlider _slider;
    Label _value;
    bool _dragging;

    public void Bind(Parameter p)
    {
        _p = p;
        GetNode<Label>("Label").Text = p.Label;
        _slider = GetNode<HSlider>("Slider");
        _value = GetNode<Label>("Value");
        if (p.Exp) (_slider.MinValue, _slider.MaxValue, _slider.Step) = (0, 1, 0.0005);
        else (_slider.MinValue, _slider.MaxValue, _slider.Step) = (p.Min, p.Max, p.Step);
        _slider.ValueChanged += OnSlider;
        _slider.DragStarted += () => _dragging = true;
        _slider.DragEnded += _ =>
        {
            _dragging = false;
            _p.Commit();
        };
        _p.Changed += Sync;
        Sync();
    }

    public override void _ExitTree()
    {
        if (_p != null) _p.Changed -= Sync;
    }

    double ToSlider(float v) => _p.Exp ? Math.Log(v / _p.Min) / Math.Log(_p.Max / _p.Min) : v;

    float FromSlider(double s)
    {
        if (!_p.Exp) return (float)s;
        double v = _p.Min * Math.Pow(_p.Max / _p.Min, s);
        return (float)(Math.Round(v / _p.Step) * _p.Step);
    }

    string Format(float v)
    {
        int decimals = Math.Clamp(-(int)Math.Floor(Math.Log10(_p.Step)), 0, 4);
        return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    void OnSlider(double s)
    {
        float v = FromSlider(s);
        _p.Set(v, !_dragging);
        _value.Text = Format(v);
    }

    void Sync()
    {
        float v = Convert.ToSingle(_p.Value);
        _slider.SetValueNoSignal(ToSlider(v));
        _value.Text = Format(v);
    }
}
