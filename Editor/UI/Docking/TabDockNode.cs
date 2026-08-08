namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// A dock region that can contain one or more panels as tabs.
/// </summary>
public sealed class TabDockNode : DockNode
{
    public IReadOnlyList<string> PanelIds { get; }
    public string? DefaultSelectedPanelId { get; }

    public TabDockNode(string id, IEnumerable<string> panelIds, string? defaultSelectedPanelId = null)
        : base(id)
    {
        var ids = panelIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray()
                  ?? throw new ArgumentNullException(nameof(panelIds));

        if (ids.Length == 0)
            throw new ArgumentException("A tab dock must contain at least one panel.", nameof(panelIds));

        PanelIds = ids;
        DefaultSelectedPanelId = ids.Contains(defaultSelectedPanelId) ? defaultSelectedPanelId : ids[0];
    }
}
