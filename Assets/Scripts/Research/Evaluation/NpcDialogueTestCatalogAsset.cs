using UnityEngine;

[CreateAssetMenu(menuName = "Research/NPC Dialogue Test Catalog")]
public sealed class NpcDialogueTestCatalogAsset : ScriptableObject
{
    [SerializeField] private string catalogId = "winter-village-v3";
    [SerializeField] private NpcDialogueTestCase[] cases = System.Array.Empty<NpcDialogueTestCase>();

    public string CatalogId => catalogId;
    public int Count => cases?.Length ?? 0;

    public NpcDialogueTestCatalog CreateSnapshot()
    {
        return new NpcDialogueTestCatalog {
            catalogId = catalogId,
            cases = cases
        }.Clone();
    }
}
