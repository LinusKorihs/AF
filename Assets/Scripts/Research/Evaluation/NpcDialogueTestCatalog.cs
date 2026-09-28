using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class NpcDialogueTestCatalog
{
    private static readonly HashSet<string> AllowedExpectations =
        new HashSet<string>(StringComparer.Ordinal) {
            "known_fact", "admit_unknown", "cross_role_boundary",
            "correct_false_premise", "reject_unverified_rumor",
            "social_in_character", "request_clarification"
        };

    public string catalogId;
    public NpcDialogueTestCase[] cases;

    public NpcDialogueTestCatalog Clone()
    {
        return JsonUtility.FromJson<NpcDialogueTestCatalog>(JsonUtility.ToJson(this));
    }

    public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(this, prettyPrint);

    public string Hash => NpcDialogueProtocol.ContentHash(ToJson(false));

    public static bool TryLoad(string json, NpcKnowledgeFile knowledge,
        out NpcDialogueTestCatalog catalog, out string problem)
    {
        catalog = null;
        problem = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            problem = "No NPC test catalog is configured.";
            return false;
        }
        try { catalog = JsonUtility.FromJson<NpcDialogueTestCatalog>(json); }
        catch (Exception ex)
        {
            problem = "Test catalog could not be parsed: " + ex.Message;
            return false;
        }
        return TryValidate(catalog, knowledge, out problem);
    }

    public static bool TryValidate(NpcDialogueTestCatalog catalog,
        NpcKnowledgeFile knowledge, out string problem)
    {
        problem = "";
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
                || !AllowedExpectations.Contains(test.expectation)
                || string.IsNullOrWhiteSpace(test.reviewNotes)
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
