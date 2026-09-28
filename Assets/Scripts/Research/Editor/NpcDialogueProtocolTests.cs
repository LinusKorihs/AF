using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
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
    public void DialogueStateMeaningRemainsASeparateManualReview()
    {
        var result = NpcDialogueProtocol.Validate(
            "{\"dialogue\":\"The delivery has not arrived.\","
            + "\"supply_state\":\"temporarily_improved\"}", true);

        Assert.That(result.StateValid, Is.True);
        Assert.That(result.IsUsable, Is.True,
            "state_valid intentionally checks the structured field, not dialogue semantics.");
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
        string json = "{\"catalogId\":\"test\",\"cases\":[{"
            + "\"id\":\"case\",\"npcId\":\"Farmer\",\"deliveryArrived\":false,"
            + "\"question\":\"Question?\",\"expectedState\":\"temporarily_improved\","
            + "\"expectation\":\"known_fact\",\"reviewNotes\":\"note\"}]}";
        var knowledge = new NpcKnowledgeFile {
            npcs = new[] { new NpcKnowledgeEntry { id = "Farmer" } }
        };

        bool valid = NpcDialogueTestCatalog.TryLoad(json, knowledge, out _, out string problem);

        Assert.That(valid, Is.False);
        Assert.That(problem, Does.Contain("conflicts"));
    }

    [Test]
    public void SelectedNpcPromptDoesNotContainOtherNpcPrivateKnowledge()
    {
        var settings = ScriptableObject.CreateInstance<LocalNpcDialogueSettings>();
        var model = new LocalNpcDialogueSettings.ModelOption { id = "test" };
        var farmer = new NpcKnowledgeEntry {
            id = "Farmer", role = "You are the farmer.",
            knowledge = "You grew wheat on your own field.", fallbackDialogue = "Fallback."
        };
        var knowledge = new NpcKnowledgeFile {
            world = "The harvest was destroyed.",
            unknownRule = "Respond naturally to greetings and do not force unrelated facts.",
            responseInstruction = "Return {state}.",
            npcs = new[] {
                farmer,
                new NpcKnowledgeEntry { id = "Knight", role = "You are the knight.",
                    knowledge = "Guard the wooden bridge.", fallbackDialogue = "Fallback." }
            },
            worldObjects = System.Array.Empty<NpcWorldObjectFact>()
        };

        string body = NpcDialogueProtocol.BuildRequest(settings, model, farmer, false,
            "Hello.", knowledge, System.Array.Empty<NpcWorldFactSource>(), 32, false, 1234);

        Assert.That(body, Does.Contain("grew wheat"));
        Assert.That(body, Does.Contain("Respond naturally to greetings"));
        Assert.That(body, Does.Not.Contain("wooden bridge"));
        Assert.That(body, Does.Contain("\"seed\":1234"));
    }

    [Test]
    public void PromptUsesOrderedSectionsAndNeutralFewShotExamples()
    {
        var settings = ScriptableObject.CreateInstance<LocalNpcDialogueSettings>();
        var model = new LocalNpcDialogueSettings.ModelOption { id = "test" };
        var knowledgeAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Scripts/Research/Data/npc_knowledge.json");
        Assert.That(knowledgeAsset, Is.Not.Null);
        var knowledge = JsonUtility.FromJson<NpcKnowledgeFile>(knowledgeAsset.text);
        var farmer = NpcDialogueProtocol.FindNpc(knowledge, "Farmer");
        Assert.That(farmer, Is.Not.Null);

        string body = NpcDialogueProtocol.BuildRequest(settings, model, farmer, false,
            "Sentinel question", knowledge, System.Array.Empty<NpcWorldFactSource>(),
            32, false, 1234);
        var messages = (JArray)JObject.Parse(body)["messages"];
        string system = messages[0]["content"].Value<string>();

        string[] sections = { "[ROLE]", "[KNOWN FACTS]",
            "[AUTHORITATIVE CURRENT STATE]", "[DIALOGUE RULES]", "[OUTPUT FORMAT]" };
        int previous = -1;
        foreach (string section in sections)
        {
            int current = system.IndexOf(section, System.StringComparison.Ordinal);
            Assert.That(current, Is.GreaterThan(previous), section + " is out of order.");
            previous = current;
        }
        string[] orderedRules = {
            "1. Answer only the current player message.",
            "2. Mention world facts, the current state, or role duties only when",
            "3. For greetings, thanks, insults, personal questions, and unclear input",
            "4. If requested information is missing",
            "5. If the input has no understandable question or statement",
            "6. You have no visual perception.",
            "7. Correct false premises using the authoritative current state.",
            "8. Never invent names, owners, quantities"
        };
        previous = -1;
        foreach (string rule in orderedRules)
        {
            int current = system.IndexOf(rule, System.StringComparison.Ordinal);
            Assert.That(current, Is.GreaterThan(previous), rule + " is missing or out of order.");
            previous = current;
        }
        Assert.That(System.Text.RegularExpressions.Regex.Matches(
            system, @"(?m)^\d+\. ").Count, Is.EqualTo(8));
        Assert.That(system, Does.Contain("you and your refer to this NPC"));
        Assert.That(system, Does.Contain("do not add world facts or duties"));
        Assert.That(system, Does.Contain("say in the first person that you do not know, then stop"));
        Assert.That(system, Does.Contain(
            "reply only with one direct clarification question ending in a question mark"));
        Assert.That(system, Does.Contain("Never claim to see, notice, observe"));
        Assert.That(system, Does.Contain("hidden technical metadata"));
        Assert.That(messages.Count, Is.EqualTo(10));

        string[] examples = messages.OfType<JObject>()
            .Where(message => message["role"].Value<string>() == "user")
            .Select(message => message["content"].Value<string>())
            .Take(4).ToArray();
        CollectionAssert.DoesNotContain(examples, "Hello.");
        CollectionAssert.DoesNotContain(examples, "How is your mother?");
        CollectionAssert.DoesNotContain(examples, "You are a useless guard.");
        CollectionAssert.DoesNotContain(examples, "?");
        Assert.That(examples[3], Is.EqualTo("Huh?"));
        var catalogAsset = AssetDatabase.LoadAssetAtPath<NpcDialogueTestCatalogAsset>(
            "Assets/Scripts/ScriptableObjects/NpcDialogueTestCatalog.asset");
        Assert.That(catalogAsset, Is.Not.Null);
        string[] benchmarkQuestions = catalogAsset.CreateSnapshot().cases
            .Select(test => test.question).ToArray();
        foreach (string example in examples)
            CollectionAssert.DoesNotContain(benchmarkQuestions, example);

        string[] exampleDialogues = messages.OfType<JObject>()
            .Where(message => message["role"].Value<string>() == "assistant")
            .Select(message => JObject.Parse(message["content"].Value<string>())
                ["dialogue"].Value<string>())
            .ToArray();
        Assert.That(exampleDialogues, Has.Length.EqualTo(4));
        foreach (string dialogue in exampleDialogues)
            Assert.That(dialogue, Does.Not.Match(
                "(?i)harvest|food|supply|delivery|bridge|cart"));
        Assert.That(exampleDialogues[3], Does.EndWith("?"));

        string currentMessage = messages[messages.Count - 1]["content"].Value<string>();
        Assert.That(currentMessage, Does.StartWith("[CURRENT PLAYER MESSAGE]"));
        Assert.That(currentMessage, Does.Contain("Answer only this message."));
        Assert.That(currentMessage, Does.Contain(
            "For social, personal, or unclear input, do not add world facts or duties."));
        Assert.That(currentMessage, Does.Contain(
            "If information is missing, say that you do not know, then stop."));
        Assert.That(currentMessage, Does.Contain(
            "If the input is unclear, ask one direct clarification question ending in a question mark."));
        Assert.That(currentMessage, Does.Contain("Never claim sensory perception."));
        Assert.That(currentMessage, Does.EndWith(
            "<player_message>Sentinel question</player_message>"));
        Assert.That(CountOccurrences(currentMessage, "Sentinel question"), Is.EqualTo(1));
        Assert.That(system, Does.Not.Contain("Sentinel question"));
        string allMessageContent = string.Join("\n", messages.OfType<JObject>()
            .Select(message => message["content"].Value<string>()));
        Assert.That(CountOccurrences(allMessageContent, "Sentinel question"), Is.EqualTo(1));
        Assert.That(body, Does.Not.Contain("\"expectation\""));
        Assert.That(body, Does.Not.Contain("\"reviewNotes\""));
        Assert.That(body, Does.Not.Contain("\"review_notes\""));
    }

    [Test]
    public void FinalProtocolUsesV9DefaultsWithoutChangingMeasurementParameters()
    {
        var defaults = ScriptableObject.CreateInstance<LocalNpcDialogueSettings>();
        var asset = AssetDatabase.LoadAssetAtPath<LocalNpcDialogueSettings>(
            "Assets/Scripts/ScriptableObjects/LocalNpcDialogueSettings.asset");
        Assert.That(asset, Is.Not.Null);

        foreach (var settings in new[] { defaults, asset })
        {
            Assert.That(settings.ProtocolId, Is.EqualTo("scene-json-v9"));
            Assert.That(settings.CsvFileName, Is.EqualTo("npc_pilot_v9.csv"));
            Assert.That(settings.SetupCsvFileName, Is.EqualTo("npc_pilot_v9_setup.csv"));
            Assert.That(settings.Temperature, Is.EqualTo(0.2f));
            Assert.That(settings.ContextTokens, Is.EqualTo(2048));
            Assert.That(settings.MaxOutputTokens, Is.EqualTo(160));
            Assert.That(settings.BatchSeedBase, Is.EqualTo(20260924));
            Assert.That(settings.Models.Select(model => model.id), Is.EqualTo(new[] {
                "qwen3-4b-instruct-2507", "gemma-3-4b-it"
            }));
        }
    }

    [Test]
    public void VersionedCatalogKeepsTwentyCasesAndAddsBiographyBoundary()
    {
        var asset = AssetDatabase.LoadAssetAtPath<NpcDialogueTestCatalogAsset>(
            "Assets/Scripts/ScriptableObjects/NpcDialogueTestCatalog.asset");
        var knowledgeAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Scripts/Research/Data/npc_knowledge.json");
        var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Scripts/Research/Data/npc_test_cases.json");
        Assert.That(asset, Is.Not.Null);
        Assert.That(knowledgeAsset, Is.Not.Null);
        Assert.That(jsonAsset, Is.Not.Null);
        var catalog = asset.CreateSnapshot();
        var knowledge = JsonUtility.FromJson<NpcKnowledgeFile>(knowledgeAsset.text);

        Assert.That(catalog.catalogId, Is.EqualTo("winter-village-v3"));
        Assert.That(catalog.cases, Has.Length.EqualTo(20));
        Assert.That(NpcDialogueTestCatalog.TryValidate(catalog, knowledge,
            out string validationProblem), Is.True, validationProblem);
        Assert.That(catalog.cases.Any(test =>
            test.id == "farmer_personal_biography_unknown"
            && test.question == "How is your mother?"), Is.True);
        Assert.That(catalog.cases.Any(test =>
            test.id == "knight_delivery_owner_unknown"), Is.False);
        Assert.That(NpcDialogueTestCatalog.TryLoad(jsonAsset.text, knowledge,
            out var jsonCatalog, out string loadProblem), Is.True, loadProblem);
        Assert.That(jsonCatalog.Hash, Is.EqualTo(catalog.Hash),
            "The Inspector catalog and JSON baseline must stay reproducible.");
    }

    [Test]
    public void CsvSeparatesDialogueStateReviewFromStructuredStateField()
    {
        Assert.That(NpcDialogueCsvLogger.HeaderLine, Does.Contain(
            "state_valid,error_code"));
        Assert.That(NpcDialogueCsvLogger.HeaderLine, Does.Contain(
            "relevance_review,dialogue_state_review,raw_response"));
    }

    [Test]
    public void CatalogCloneAndJsonRoundTripAreIndependent()
    {
        var original = new NpcDialogueTestCatalog {
            catalogId = "catalog",
            cases = new[] { new NpcDialogueTestCase {
                id = "minimal", npcId = "Knight", deliveryArrived = true,
                question = "?", expectedState = "temporarily_improved",
                expectation = "request_clarification", reviewNotes = "Ask for clarification."
            } }
        };
        var knowledge = new NpcKnowledgeFile {
            npcs = new[] { new NpcKnowledgeEntry { id = "Knight" } }
        };

        var clone = original.Clone();
        clone.cases[0].question = "Changed";
        bool loaded = NpcDialogueTestCatalog.TryLoad(original.ToJson(), knowledge,
            out var roundTrip, out string problem);

        Assert.That(original.cases[0].question, Is.EqualTo("?"));
        Assert.That(loaded, Is.True, problem);
        Assert.That(roundTrip.cases[0].expectation, Is.EqualTo("request_clarification"));
        Assert.That(roundTrip.Hash, Is.EqualTo(original.Hash));
    }

    [Test]
    public void BatchOrderAndSeedsAreStable()
    {
        CollectionAssert.AreEqual(
            new[] { LocalNpcBackend.VulkanGpu, LocalNpcBackend.Cpu },
            NpcDialogueBatchRunner.BuildBackendSequence(true));
        Assert.That(NpcDialogueBatchRunner.ComputeSamplingSeed(20260924, 1, 0),
            Is.EqualTo(20261924));
        Assert.That(NpcDialogueBatchRunner.ComputeSamplingSeed(20260924, 2, 0),
            Is.Not.EqualTo(NpcDialogueBatchRunner.ComputeSamplingSeed(20260924, 1, 0)));
        Assert.That(NpcDialogueBatchRunner.CalculateTotalRequests(20, 10, 2, true),
            Is.EqualTo(800));
    }

    [Test]
    public void TechnicalFailureUsesFallbackWithoutBecomingUsable()
    {
        var result = new NpcDialogueResult {
            Request = new NpcDialogueRequest { NpcId = "Knight" },
            Validation = new NpcDialogueValidation {
                ErrorCode = NpcDialogueErrorCode.Timeout, Error = "Timed out."
            }
        };
        var knight = new NpcKnowledgeEntry {
            fallbackDialogue = "I cannot give you a reliable answer right now."
        };

        NpcDialoguePresentation.Apply(result, knight);

        Assert.That(result.IsUsable, Is.False);
        Assert.That(result.FallbackUsed, Is.True);
        Assert.That(result.FallbackReason, Is.EqualTo("Timeout"));
        Assert.That(result.PresentationDialogue,
            Is.EqualTo("I cannot give you a reliable answer right now."));
    }

    private static int CountOccurrences(string value, string search)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(search, offset, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }
        return count;
    }
}
