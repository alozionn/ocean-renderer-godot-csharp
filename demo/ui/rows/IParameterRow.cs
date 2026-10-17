using Ocean.Core.Parameters;

namespace OceanDemo.UI;

/// <summary>A row scene's script: binds both ways to one parameter and unsubscribes when it leaves the tree.</summary>
public interface IParameterRow
{
    void Bind(Parameter parameter);
}
