using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class NpcDialogueActor : MonoBehaviour
{
    [SerializeField, Tooltip("Matches an NPC id in npc_knowledge.json.")]
    private string npcId;
    [SerializeField, Tooltip("World-space offset of the thinking bubble above this NPC.")]
    private Vector3 bubbleOffset = new Vector3(0f, 2.2f, 0f);

    private GameObject bubbleRoot;

    public string NpcId => npcId;

    private void Awake()
    {
        EnsureBubble();
        ShowThinking(false);
    }

    public void ShowThinking(bool visible)
    {
        EnsureBubble();
        bubbleRoot.SetActive(visible);
    }

    private void LateUpdate()
    {
        if (bubbleRoot == null || !bubbleRoot.activeSelf || Camera.main == null) return;
        bubbleRoot.transform.rotation = Camera.main.transform.rotation;
    }

    private void EnsureBubble()
    {
        if (bubbleRoot != null) return;
        bubbleRoot = new GameObject("Thinking Bubble", typeof(RectTransform), typeof(Canvas));
        bubbleRoot.transform.SetParent(transform, false);
        bubbleRoot.transform.localPosition = bubbleOffset;
        bubbleRoot.transform.localScale = Vector3.one * 0.008f;

        var canvas = bubbleRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        var rect = bubbleRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(150f, 64f);

        var panel = new GameObject("Background", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(bubbleRoot.transform, false);
        Stretch(panel.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.92f);

        var dots = new GameObject("Dots", typeof(RectTransform), typeof(TextMeshProUGUI));
        dots.transform.SetParent(panel.transform, false);
        Stretch(dots.GetComponent<RectTransform>(), new Vector2(8f, 4f), new Vector2(-8f, -4f));
        var text = dots.GetComponent<TextMeshProUGUI>();
        text.font = NpcDialogueUiResources.RuntimeFont;
        text.text = "...";
        text.fontSize = 38f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
    }

    private static void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
    }
}
