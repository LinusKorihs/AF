using UnityEngine;

// The GameObject's active state controls the matching fact from npc_knowledge.json.
public sealed class NpcWorldFactSource : MonoBehaviour
{
    [SerializeField, Tooltip("Matches a worldObjects ID in the knowledge JSON. This GameObject's active state selects its active or inactive fact.")]
    private string factId = "supply_delivery";
    [SerializeField, Tooltip("Label shown for this world object in the dialogue panel.")]
    private string displayName = "Horse with supplies";

    public string FactId => factId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? factId : displayName;
}
