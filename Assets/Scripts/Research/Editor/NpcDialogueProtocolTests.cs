using NUnit.Framework;
using UnityEngine;

public sealed class NpcDialogueProtocolTests
{
    [Test]
    public void ValidResponsePassesAllChecks()
    {
        var result = NpcDialogueProtocol.Validate(
            "{\"dialogue\":\"Food is scarce.\",\"supply_state\":\"critical\"}", false);

        Assert.That(result.SyntaxValid, Is.True);
        Assert.That(result.StructureValid, Is.True);
        Assert.That(result.StateValid, Is.True);
        Assert.That(result.IsUsable, Is.True);
    }

    [TestCase("{\"dialogue\":\"x\",\"supply_state\":\"critical\",\"extra\":1}",
        NpcDialogueErrorCode.UnexpectedFields)]
    [TestCase("{\"dialogue\":\"x\"}", NpcDialogueErrorCode.MissingField)]
    [TestCase("{\"dialogue\":\"x\",\"supply_state\":\"unknown\"}",
        NpcDialogueErrorCode.InvalidValue)]
    [TestCase("prefix {\"dialogue\":\"x\",\"supply_state\":\"critical\"}",
        NpcDialogueErrorCode.InvalidJson)]
    public void StrictValidationRejectsInvalidResponses(string raw, NpcDialogueErrorCode expected)
    {
        var result = NpcDialogueProtocol.Validate(raw, false);
        Assert.That(result.IsUsable, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(expected));
    }

    [Test]
    public void StateMismatchIsSeparateFromJsonStructure()
    {
        var result = NpcDialogueProtocol.Validate(
            "{\"dialogue\":\"The cart arrived.\",\"supply_state\":\"temporarily_improved\"}", false);

        Assert.That(result.SyntaxValid, Is.True);
        Assert.That(result.StructureValid, Is.True);
        Assert.That(result.StateValid, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(NpcDialogueErrorCode.StateMismatch));
    }

    [Test]
    public void DialoguePaginationUsesAtMostTwoPages()
    {
        string[] pages = NpcDialoguePlayerController.SplitIntoPages(
            "First sentence. Second sentence! Third sentence?");

        Assert.That(pages, Has.Length.EqualTo(2));
        Assert.That(pages[0], Is.EqualTo("First sentence."));
        Assert.That(pages[1], Is.EqualTo("Second sentence! Third sentence?"));
    }

    [Test]
    public void CatalogRejectsStateThatConflictsWithDeliveryFlag()
    {
        var asset = new TextAsset("{\"catalogId\":\"test\",\"cases\":[{"
            + "\"id\":\"case\",\"npcId\":\"Farmer\",\"deliveryArrived\":false,"
            + "\"question\":\"Question?\",\"expectedState\":\"temporarily_improved\","
            + "\"expectation\":\"known_fact\",\"reviewNotes\":\"\"}]}");
        var knowledge = new NpcKnowledgeFile {
            npcs = new[] { new NpcKnowledgeEntry { id = "Farmer" } }
        };

        bool valid = NpcDialogueTestCatalog.TryLoad(asset, knowledge, out _, out string problem);

        Assert.That(valid, Is.False);
        Assert.That(problem, Does.Contain("conflicts"));
    }
}
