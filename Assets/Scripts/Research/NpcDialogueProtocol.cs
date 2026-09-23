using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public sealed class NpcKnowledgeFile
{
    public string world;
    public string unknownRule;
    public string responseInstruction;
    public NpcKnowledgeEntry[] npcs;
    public NpcWorldObjectFact[] worldObjects;
}

[Serializable]
public sealed class NpcKnowledgeEntry
{
    public string id;
    public string displayName;
    public string role;
    public string knowledge;
}

[Serializable]
public sealed class NpcWorldObjectFact
{
    public string id;
    public string activeFact;
    public string inactiveFact;
}

public static class NpcDialogueProtocol
{
    private const string CriticalState = "critical";
    private const string ImprovedState = "temporarily_improved";

    public static string ExpectedState(bool delivered) => delivered ? ImprovedState : CriticalState;

    public static NpcKnowledgeEntry FindNpc(NpcKnowledgeFile knowledge, string npcId)
    {
        if (knowledge?.npcs == null) return null;
        foreach (var npc in knowledge.npcs)
            if (npc != null && string.Equals(npc.id, npcId, StringComparison.Ordinal))
                return npc;
        return null;
    }

    public static NpcWorldFactSource[] FindWorldSources(MonoBehaviour owner)
    {
        var all = UnityEngine.Object.FindObjectsByType<NpcWorldFactSource>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var inScene = new List<NpcWorldFactSource>();
        foreach (var source in all)
            if (source.gameObject.scene == owner.gameObject.scene)
                inScene.Add(source);
        inScene.Sort((a, b) => string.CompareOrdinal(a.FactId, b.FactId));
        return inScene.ToArray();
    }

    public static bool IsFactActive(NpcWorldFactSource[] sources, string factId)
    {
        foreach (var source in sources)
            if (source != null && source.FactId == factId)
                return source.gameObject.activeInHierarchy;
        return false;
    }

    public static string WorldStateSnapshot(NpcWorldFactSource[] sources)
    {
        var states = new List<string>();
        foreach (var source in sources)
            if (source != null)
                states.Add(source.FactId + "="
                    + (source.gameObject.activeInHierarchy ? "active" : "inactive"));
        return string.Join(";", states);
    }

    public static bool TryLoadKnowledge(TextAsset json, NpcWorldFactSource[] sources,
        string deliveryFactId, out NpcKnowledgeFile knowledge, out string problem)
    {
        knowledge = null;
        problem = "";
        if (json == null)
        {
            problem = "No knowledge JSON is assigned in the prefab.";
            return false;
        }
        try { knowledge = JsonUtility.FromJson<NpcKnowledgeFile>(json.text); }
        catch (Exception ex)
        {
            problem = "Knowledge JSON could not be parsed: " + ex.Message;
            return false;
        }
        if (knowledge == null || string.IsNullOrWhiteSpace(knowledge.world)
            || string.IsNullOrWhiteSpace(knowledge.unknownRule)
            || string.IsNullOrWhiteSpace(knowledge.responseInstruction)
            || knowledge.npcs == null || knowledge.npcs.Length == 0
            || knowledge.worldObjects == null)
        {
            problem = "world, unknownRule, responseInstruction, npcs or worldObjects is missing.";
            return false;
        }
        foreach (var npc in knowledge.npcs)
            if (npc == null || string.IsNullOrWhiteSpace(npc.id)
                || string.IsNullOrWhiteSpace(npc.role)
                || string.IsNullOrWhiteSpace(npc.knowledge))
            {
                problem = "An NPC entry is incomplete.";
                return false;
            }

        bool hasDeliverySource = false;
        var ids = new HashSet<string>();
        foreach (var source in sources)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.FactId)
                || !ids.Add(source.FactId))
            {
                problem = "A world fact source is missing an ID or has a duplicate ID.";
                return false;
            }
            if (source.FactId == deliveryFactId) hasDeliverySource = true;
            bool mapped = false;
            foreach (var fact in knowledge.worldObjects)
                if (fact != null && fact.id == source.FactId
                    && !string.IsNullOrWhiteSpace(fact.activeFact)
                    && !string.IsNullOrWhiteSpace(fact.inactiveFact))
                    mapped = true;
            if (!mapped)
            {
                problem = "The knowledge JSON has no facts for " + source.FactId + ".";
                return false;
            }
        }
        if (!hasDeliverySource)
        {
            problem = "The delivery fact source is missing from the scene: " + deliveryFactId + ".";
            return false;
        }
        return true;
    }

    public static string BuildRequest(LocalNpcDialogueSettings settings,
        LocalNpcDialogueSettings.ModelOption model, NpcKnowledgeEntry npc,
        bool delivered, string playerQuestion, NpcKnowledgeFile knowledge,
        NpcWorldFactSource[] sources, int maxTokens, bool stream)
    {
        var system = new StringBuilder();
        system.Append(npc.role).Append(' ').Append(knowledge.world).Append(' ')
            .Append(npc.knowledge).Append(' ');
        var includedFacts = new HashSet<string>();
        foreach (var source in sources)
        {
            if (source == null || !includedFacts.Add(source.FactId)) continue;
            foreach (var fact in knowledge.worldObjects)
            {
                if (fact == null || fact.id != source.FactId) continue;
                system.Append(source.gameObject.activeInHierarchy ? fact.activeFact : fact.inactiveFact)
                    .Append(' ');
                break;
            }
        }
        system.Append(knowledge.unknownRule).Append(' ')
            .Append(knowledge.responseInstruction.Replace("{state}",
                delivered ? ImprovedState : CriticalState));

        return "{\"model\":\"" + EscapeJson(model.id) + "\","
            + "\"messages\":[{\"role\":\"system\",\"content\":\"" + EscapeJson(system.ToString())
            + "\"},{\"role\":\"user\",\"content\":\"" + EscapeJson(playerQuestion) + "\"}],"
            + "\"temperature\":" + settings.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"max_tokens\":" + maxTokens
            + (stream ? ",\"stream\":true,\"stream_options\":{\"include_usage\":true},"
                : ",\"stream\":false,")
            + "\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{"
            + "\"name\":\"npc_reply\",\"schema\":{\"type\":\"object\",\"properties\":{"
            + "\"dialogue\":{\"type\":\"string\"},"
            + "\"supply_state\":{\"type\":\"string\",\"enum\":[\"critical\",\"temporarily_improved\"]}},"
            + "\"required\":[\"dialogue\",\"supply_state\"],\"additionalProperties\":false}}}}";
    }

    public static NpcDialogueValidation Validate(string raw, bool delivered)
    {
        var result = new NpcDialogueValidation {
            ErrorCode = NpcDialogueErrorCode.InvalidJson,
            Error = "Response is not a JSON object."
        };
        if (string.IsNullOrWhiteSpace(raw))
        {
            result.Error = "Response JSON is empty.";
            return result;
        }

        JToken token;
        try
        {
            using (var stringReader = new StringReader(raw))
            using (var reader = new JsonTextReader(stringReader))
                while (reader.Read())
                    if (reader.TokenType == JsonToken.Comment)
                        throw new JsonReaderException("JSON comments are not allowed.");
            token = JToken.Parse(raw, new JsonLoadSettings {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Ignore
            });
        }
        catch (JsonException ex)
        {
            result.Error = "Response JSON could not be parsed: " + ex.Message;
            return result;
        }

        result.SyntaxValid = true;
        if (!(token is JObject obj))
        {
            result.Error = "Response root must be a JSON object.";
            return result;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.Properties()) names.Add(property.Name);
        if (!names.Contains("dialogue") || !names.Contains("supply_state"))
        {
            result.ErrorCode = NpcDialogueErrorCode.MissingField;
            result.Error = "Response must contain dialogue and supply_state.";
            return result;
        }
        if (names.Count != 2)
        {
            result.ErrorCode = NpcDialogueErrorCode.UnexpectedFields;
            result.Error = "Response contains fields other than dialogue and supply_state.";
            return result;
        }

        JToken dialogue = obj["dialogue"];
        JToken state = obj["supply_state"];
        if (dialogue?.Type != JTokenType.String || state?.Type != JTokenType.String
            || string.IsNullOrWhiteSpace(dialogue.Value<string>()))
        {
            result.ErrorCode = NpcDialogueErrorCode.InvalidValue;
            result.Error = "dialogue and supply_state must be non-empty strings.";
            return result;
        }

        result.Dialogue = dialogue.Value<string>().Trim();
        result.SupplyState = state.Value<string>();
        if (result.SupplyState != CriticalState && result.SupplyState != ImprovedState)
        {
            result.ErrorCode = NpcDialogueErrorCode.InvalidValue;
            result.Error = "supply_state is not an allowed value.";
            return result;
        }

        result.StructureValid = true;
        result.StateValid = result.SupplyState == ExpectedState(delivered);
        if (!result.StateValid)
        {
            result.ErrorCode = NpcDialogueErrorCode.StateMismatch;
            result.Error = "supply_state does not match Unity's world state.";
            return result;
        }

        result.ErrorCode = NpcDialogueErrorCode.None;
        result.Error = "";
        return result;
    }

    public static string KnowledgeHash(string content)
    {
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private static string EscapeJson(string value)
    {
        var escaped = new StringBuilder();
        foreach (char character in value)
        {
            switch (character)
            {
                case '"': escaped.Append("\\\""); break;
                case '\\': escaped.Append("\\\\"); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                default:
                    if (character < 32) escaped.Append("\\u" + ((int)character).ToString("x4"));
                    else escaped.Append(character);
                    break;
            }
        }
        return escaped.ToString();
    }
}
