using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class NpcDialogueTestCatalog
{
    public string catalogId;
    public NpcDialogueTestCase[] cases;

    public static bool TryLoad(TextAsset asset, NpcKnowledgeFile knowledge,
        out NpcDialogueTestCatalog catalog, out string problem)
    {
        catalog = null;
        problem = "";
        if (asset == null)
        {
            problem = "No NPC test catalog is assigned.";
            return false;
        }
        try { catalog = JsonUtility.FromJson<NpcDialogueTestCatalog>(asset.text); }
        catch (Exception ex)
        {
            problem = "Test catalog could not be parsed: " + ex.Message;
            return false;
        }
        if (catalog == null || string.IsNullOrWhiteSpace(catalog.catalogId)
            || catalog.cases == null || catalog.cases.Length == 0)
        {
            problem = "catalogId or cases is missing.";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in catalog.cases)
        {
            if (test == null || string.IsNullOrWhiteSpace(test.id)
                || !ids.Add(test.id) || string.IsNullOrWhiteSpace(test.npcId)
                || string.IsNullOrWhiteSpace(test.question)
                || string.IsNullOrWhiteSpace(test.expectation)
                || NpcDialogueProtocol.FindNpc(knowledge, test.npcId) == null)
            {
                problem = "A test case is incomplete, duplicated, or references an unknown NPC.";
                return false;
            }
            string expected = NpcDialogueProtocol.ExpectedState(test.deliveryArrived);
            if (!string.Equals(test.expectedState, expected, StringComparison.Ordinal))
            {
                problem = "Test case " + test.id + " has an expected state that conflicts with deliveryArrived.";
                return false;
            }
        }
        return true;
    }
}

[Serializable]
public sealed class NpcDialogueTestCase
{
    public string id;
    public string npcId;
    public bool deliveryArrived;
    public string question;
    public string expectedState;
    public string expectation;
    public string reviewNotes;
}
