namespace TACTIX.Engine.Runtime.ECS;

/// <summary>Editor/runtime-neutral parent link. Local model hierarchy survives independently of source format.</summary>
public struct ParentComponent
{
    public int ParentEntityId;
    public ParentComponent(int parentEntityId) => ParentEntityId = parentEntityId;
}
