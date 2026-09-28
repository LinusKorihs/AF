using System;

public static class NpcDialoguePresentation
{
    public static void Apply(NpcDialogueResult result, NpcKnowledgeEntry npc)
    {
        if (result == null) return;
        if (result.IsUsable)
        {
            result.PresentationDialogue = result.Validation.Dialogue;
            result.FallbackUsed = false;
            result.FallbackReason = "";
            return;
        }

        string npcId = result.Request?.NpcId ?? "";
        result.PresentationDialogue = !string.IsNullOrWhiteSpace(npc?.fallbackDialogue)
            ? npc.fallbackDialogue
            : string.Equals(npcId, "Knight", StringComparison.Ordinal)
                ? "I cannot give you a reliable answer right now."
                : "Sorry, I cannot answer that right now.";
        result.FallbackUsed = true;
        result.FallbackReason = result.Validation?.ErrorCode.ToString() ?? "Unknown";
    }
}
