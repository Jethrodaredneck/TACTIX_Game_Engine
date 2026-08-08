namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// Base node for a declarative editor docking tree.
/// </summary>
public abstract class DockNode
{
    public string Id { get; }

    protected DockNode(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }
}
