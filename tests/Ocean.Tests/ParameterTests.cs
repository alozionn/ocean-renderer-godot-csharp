using Ocean.Core.Parameters;

namespace Ocean.Tests;

public class ParameterTests
{
    [Test]
    public async Task SettingRaisesChangedOnceAndNotForTheSameValue()
    {
        float value = 1;
        var p = Parameter.Float("x", "X", "", 0, () => value, v => value = v, 0, 10, 0.1);
        int raised = 0;
        p.Changed += () => raised++;
        p.Set(2.5f);
        await Assert.That(raised).IsEqualTo(1);
        await Assert.That(value).IsEqualTo(2.5f);
        p.Set(2.5f);
        p.Set(2.5); // a double from a UI control is the same value
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task OnReleaseAppliesOnlyOnCommit()
    {
        float value = 1;
        int applied = 0;
        var p = Parameter.Float("x", "X", "", 0, () => value, v => { value = v; applied++; }, 0, 10, 0.1, mode: ApplyMode.OnRelease);
        p.Set(3f, commit: false);
        p.Set(4f, commit: false);
        await Assert.That(applied).IsEqualTo(0);
        await Assert.That(p.Value).IsEqualTo(4f);
        p.Commit();
        await Assert.That(applied).IsEqualTo(1);
        await Assert.That(value).IsEqualTo(4f);
    }

    [Test]
    public async Task RefreshRaisesOnlyWhenTheSourceChanged()
    {
        string sky = "a";
        var p = Parameter.Enum("sky", "Sky", "", 0, new[] { "a", "b" }, () => sky, v => sky = v);
        int raised = 0;
        p.Changed += () => raised++;
        p.Refresh();
        await Assert.That(raised).IsEqualTo(0);
        sky = "b"; // changed behind the parameter's back, e.g. by a preset load
        p.Refresh();
        p.Refresh();
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task ActionsRunAndReadoutsCannotBeSet()
    {
        int runs = 0;
        var a = Parameter.Action("go", "Go", "", 0, () => runs++);
        a.Invoke();
        await Assert.That(runs).IsEqualTo(1);
        var r = Parameter.Readout("fps", "FPS", "", 0, () => "60");
        r.Set("1");
        await Assert.That(r.Value).IsEqualTo("60");
    }
}
