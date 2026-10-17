#nullable disable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ocean.Core.Parameters;

/// <summary>The closed set of parameter kinds a presenter has to support.</summary>
public enum ParameterKind { Float, Int, Bool, Enum, Color, Action, Readout }

/// <summary>Live: every change applies. OnRelease: changes apply only on commit (e.g. when a slider drag ends).</summary>
public enum ApplyMode { Live, OnRelease }

/// <summary>
/// One live knob. The parameter is the single source of truth: presenters never cache values, they listen to
/// <see cref="Changed"/>, which code changes, preset loads and UI edits all raise. Values are float, int, bool,
/// string (enum option), Vector3 (colour) or string (readout).
/// </summary>
public sealed class Parameter
{
    readonly Func<object> _get;
    readonly Action<object> _set;
    object _last;
    object _pending;

    Parameter(string id, string label, string group, int order, ParameterKind kind, Func<object> get, Action<object> set)
    {
        Id = id;
        Label = label;
        Group = group;
        Order = order;
        Kind = kind;
        _get = get;
        _set = set;
        _last = get?.Invoke();
    }

    public string Id { get; }
    public string Label { get; }
    public string Group { get; }
    public int Order { get; }
    public ParameterKind Kind { get; }
    public ApplyMode Mode { get; private init; }
    public double Min { get; private init; }
    public double Max { get; private init; }
    public double Step { get; private init; }
    public bool Exp { get; private init; }
    public IReadOnlyList<string> Options { get; private init; } = Array.Empty<string>();

    public event Action Changed;

    /// <summary>The current value; while an OnRelease edit is pending, the pending value.</summary>
    public object Value => _pending ?? _get?.Invoke();

    /// <summary>Sets the value. OnRelease parameters only stage it unless <paramref name="commit"/> is true.</summary>
    public void Set(object value, bool commit = true)
    {
        if (Kind is ParameterKind.Action or ParameterKind.Readout) return;
        if (Mode == ApplyMode.OnRelease && !commit)
        {
            _pending = value;
            return;
        }
        _pending = null;
        value = Normalize(value);
        if (Equals(value, _get())) return;
        _set(value);
        _last = _get();
        Changed?.Invoke();
    }

    object Normalize(object v) => Kind switch
    {
        ParameterKind.Float => Convert.ToSingle(v, System.Globalization.CultureInfo.InvariantCulture),
        ParameterKind.Int => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture),
        _ => v,
    };

    /// <summary>Applies a staged OnRelease value.</summary>
    public void Commit()
    {
        if (_pending != null) Set(_pending);
    }

    /// <summary>Runs an Action parameter.</summary>
    public void Invoke()
    {
        if (Kind == ParameterKind.Action) _set(null);
    }

    /// <summary>Raises <see cref="Changed"/> if the underlying value changed behind the parameter's back (code, preset load).</summary>
    public void Refresh()
    {
        if (_get == null) return;
        var now = _get();
        if (Equals(now, _last)) return;
        _last = now;
        Changed?.Invoke();
    }

    public static Parameter Float(string id, string label, string group, int order, Func<float> get, Action<float> set,
        double min, double max, double step, bool exp = false, ApplyMode mode = ApplyMode.Live) =>
        new(id, label, group, order, ParameterKind.Float, () => get(), v => set(Convert.ToSingle(v)))
        { Min = min, Max = max, Step = step, Exp = exp, Mode = mode };

    public static Parameter Int(string id, string label, string group, int order, Func<int> get, Action<int> set,
        int min, int max, ApplyMode mode = ApplyMode.Live) =>
        new(id, label, group, order, ParameterKind.Int, () => get(), v => set(Convert.ToInt32(v))) { Min = min, Max = max, Step = 1, Mode = mode };

    public static Parameter Bool(string id, string label, string group, int order, Func<bool> get, Action<bool> set) =>
        new(id, label, group, order, ParameterKind.Bool, () => get(), v => set((bool)v));

    public static Parameter Enum(string id, string label, string group, int order, IReadOnlyList<string> options,
        Func<string> get, Action<string> set, ApplyMode mode = ApplyMode.Live) =>
        new(id, label, group, order, ParameterKind.Enum, () => get(), v => set((string)v)) { Options = options, Mode = mode };

    public static Parameter Color(string id, string label, string group, int order, Func<Vector3> get, Action<Vector3> set) =>
        new(id, label, group, order, ParameterKind.Color, () => get(), v => set((Vector3)v));

    public static Parameter Action(string id, string label, string group, int order, Action run) =>
        new(id, label, group, order, ParameterKind.Action, null, _ => run());

    public static Parameter Readout(string id, string label, string group, int order, Func<string> get) =>
        new(id, label, group, order, ParameterKind.Readout, () => get(), null);
}

/// <summary>A titled, ordered list of parameters. Nothing else.</summary>
public sealed class ParameterSet
{
    public ParameterSet(string title, IReadOnlyList<Parameter> parameters)
    {
        Title = title;
        Parameters = parameters;
    }

    public string Title { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
}
