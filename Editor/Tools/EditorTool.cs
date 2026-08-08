using TACTIX.Editor.UI.Docking;

namespace TACTIX.Editor.Tools;

/// <summary>
/// Base contract for an editor tool that contributes panels and a default dock tree.
/// </summary>
public abstract class EditorTool
{
    public abstract string ToolId { get; }

    public abstract void RegisterPanels(DockManager dockManager);
    public abstract DockNode CreateDefaultLayout();
}
