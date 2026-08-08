namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// Binary split in the dock tree. Vertical=true means left/right; false means
/// top/bottom. Ratio is the first child's initial share of the available extent.
/// </summary>
public sealed class SplitDockNode : DockNode
{
    public bool Vertical { get; }
    public double Ratio { get; set; }
    public DockNode First { get; }
    public DockNode Second { get; }

    public SplitDockNode(string id, bool vertical, double ratio, DockNode first, DockNode second)
        : base(id)
    {
        Vertical = vertical;
        Ratio = Math.Clamp(ratio, 0.05, 0.95);
        First = first ?? throw new ArgumentNullException(nameof(first));
        Second = second ?? throw new ArgumentNullException(nameof(second));
    }
}
