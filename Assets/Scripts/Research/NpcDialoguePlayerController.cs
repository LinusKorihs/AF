using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[Serializable]
public sealed class NpcHotkeyBinding
{
    public string npcId;
    public Key key;
}

public enum NpcPlayerDialogueState
{
    Closed,
    QuestionInput,
    WaitingForResponse,
    ShowingResponse
}

public sealed class NpcDialoguePlayerController : MonoBehaviour
{
    private LocalNpcDialogueService service;
    private NpcHotkeyBinding[] bindings;
    private Key deliveryKey;
    private readonly Dictionary<string, NpcDialogueActor> actors =
        new Dictionary<string, NpcDialogueActor>(StringComparer.Ordinal);
    private NpcDialogueActor activeActor;
    private string activeNpcId;
    private string[] pages = Array.Empty<string>();
    private int pageIndex;

    private GameObject canvasRoot;
    private GameObject dialoguePanel;
    private TextMeshProUGUI startupStatus;
    private TextMeshProUGUI npcName;
    private TextMeshProUGUI bodyText;
    private TextMeshProUGUI stateText;
    private TextMeshProUGUI hintText;
    private TMP_InputField input;

    public NpcPlayerDialogueState State { get; private set; } = NpcPlayerDialogueState.Closed;

    public void Configure(LocalNpcDialogueService configuredService,
        NpcHotkeyBinding[] configuredBindings, Key configuredDeliveryKey)
    {
        service = configuredService;
        bindings = configuredBindings;
        deliveryKey = configuredDeliveryKey;
        RefreshActors();
        EnsureUi();
    }

    public void SetPlayerModeEnabled(bool enabled)
    {
        EnsureUi();
        canvasRoot.SetActive(enabled);
        if (!enabled) CloseDialogue();
    }

    private void Update()
    {
        if (canvasRoot == null || !canvasRoot.activeSelf || service == null) return;
        UpdateStartupStatus();
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (State == NpcPlayerDialogueState.Closed)
        {
            if (bindings != null)
                foreach (var binding in bindings)
                    if (binding != null && binding.key != Key.None
                        && keyboard[binding.key].wasPressedThisFrame)
                    {
                        OpenDialogue(binding.npcId);
                        return;
                    }
            if (deliveryKey != Key.None && keyboard[deliveryKey].wasPressedThisFrame
                && !service.DeliveryArrived)
            {
                service.SetDeliveryActive(true);
                Debug.Log("[NPC Pilot] The supply delivery arrived via player input.");
            }
            return;
        }

        if (State == NpcPlayerDialogueState.QuestionInput)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) CloseDialogue();
            else if (keyboard.enterKey.wasPressedThisFrame
                && !string.IsNullOrWhiteSpace(input.text)) StartCoroutine(Ask());
            return;
        }
        if (State == NpcPlayerDialogueState.ShowingResponse)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) CloseDialogue();
            else if (keyboard.enterKey.wasPressedThisFrame) ShowNextPage();
        }
    }

    private void OpenDialogue(string npcId)
    {
        RefreshActors();
        activeNpcId = npcId;
        actors.TryGetValue(npcId, out activeActor);
        dialoguePanel.SetActive(true);
        npcName.text = service.DisplayName(npcId);
        bodyText.text = "";
        stateText.text = service.IsReady ? "Ask your question." : service.Status;
        hintText.text = "Enter: ask   Esc: close";
        input.text = "";
        input.interactable = true;
        input.gameObject.SetActive(true);
        State = NpcPlayerDialogueState.QuestionInput;
        StartCoroutine(FocusInputNextFrame());
    }

    private IEnumerator FocusInputNextFrame()
    {
        yield return null;
        input.Select();
        input.ActivateInputField();
    }

    private IEnumerator Ask()
    {
        if (State != NpcPlayerDialogueState.QuestionInput) yield break;
        State = NpcPlayerDialogueState.WaitingForResponse;
        string question = input.text.Trim();
        input.DeactivateInputField();
        input.interactable = false;
        stateText.text = npcName.text + " is thinking...";
        hintText.text = "Please wait for the checked response.";
        activeActor?.ShowThinking(true);

        NpcDialogueResult result = null;
        var request = new NpcDialogueRequest {
            RunId = "player",
            Phase = "player",
            NpcId = activeNpcId,
            Question = question,
            DeliveryArrived = service.DeliveryArrived,
            ExpectedState = NpcDialogueProtocol.ExpectedState(service.DeliveryArrived)
        };
        yield return service.Send(request, value => result = value);
        activeActor?.ShowThinking(false);

        string visible = result != null && result.IsUsable
            ? result.Validation.Dialogue : "The NPC could not answer reliably.";
        pages = SplitIntoPages(visible);
        pageIndex = 0;
        bodyText.text = pages.Length > 0 ? pages[0] : visible;
        stateText.text = result != null && result.IsUsable
            ? "Checked response." : result?.Validation?.Error ?? "Request failed.";
        input.gameObject.SetActive(false);
        hintText.text = pages.Length > 1 ? "Enter: next" : "Enter: close";
        State = NpcPlayerDialogueState.ShowingResponse;
    }

    private void ShowNextPage()
    {
        pageIndex++;
        if (pageIndex >= pages.Length)
        {
            CloseDialogue();
            return;
        }
        bodyText.text = pages[pageIndex];
        hintText.text = pageIndex + 1 < pages.Length ? "Enter: next" : "Enter: close";
    }

    private void CloseDialogue()
    {
        activeActor?.ShowThinking(false);
        activeActor = null;
        activeNpcId = "";
        pages = Array.Empty<string>();
        pageIndex = 0;
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        State = NpcPlayerDialogueState.Closed;
    }

    public static string[] SplitIntoPages(string dialogue)
    {
        if (string.IsNullOrWhiteSpace(dialogue)) return Array.Empty<string>();
        string[] sentences = Regex.Split(dialogue.Trim(), @"(?<=[.!?])\s+");
        if (sentences.Length <= 2) return sentences;
        var second = new List<string>();
        for (int i = 1; i < sentences.Length; i++) second.Add(sentences[i]);
        return new[] { sentences[0], string.Join(" ", second) };
    }

    private void RefreshActors()
    {
        actors.Clear();
        foreach (var actor in FindObjectsByType<NpcDialogueActor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (actor != null && !string.IsNullOrWhiteSpace(actor.NpcId))
                actors[actor.NpcId] = actor;
    }

    private void UpdateStartupStatus()
    {
        if (startupStatus == null) return;
        bool show = State == NpcPlayerDialogueState.Closed && !service.IsReady;
        startupStatus.gameObject.SetActive(show);
        if (show) startupStatus.text = service.Status;
    }

    private void EnsureUi()
    {
        if (canvasRoot != null) return;
        canvasRoot = new GameObject("Player Dialogue Canvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot.transform.SetParent(transform, false);
        var canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();

        startupStatus = CreateText("Startup Status", canvasRoot.transform, 24f,
            TextAlignmentOptions.TopLeft, Color.white);
        var startupRect = startupStatus.rectTransform;
        startupRect.anchorMin = new Vector2(0f, 1f);
        startupRect.anchorMax = new Vector2(0f, 1f);
        startupRect.pivot = new Vector2(0f, 1f);
        startupRect.anchoredPosition = new Vector2(24f, -24f);
        startupRect.sizeDelta = new Vector2(760f, 70f);

        dialoguePanel = new GameObject("Dialogue Panel", typeof(RectTransform), typeof(Image));
        dialoguePanel.transform.SetParent(canvasRoot.transform, false);
        var panelRect = dialoguePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.08f, 0.04f);
        panelRect.anchorMax = new Vector2(0.92f, 0.34f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        dialoguePanel.GetComponent<Image>().color = new Color(0.035f, 0.05f, 0.08f, 0.94f);

        npcName = CreateText("NPC Name", dialoguePanel.transform, 34f,
            TextAlignmentOptions.TopLeft, new Color(0.8f, 0.9f, 1f));
        SetAnchors(npcName.rectTransform, new Vector2(0.04f, 0.76f), new Vector2(0.96f, 0.95f));
        bodyText = CreateText("Dialogue", dialoguePanel.transform, 30f,
            TextAlignmentOptions.TopLeft, Color.white);
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        SetAnchors(bodyText.rectTransform, new Vector2(0.04f, 0.38f), new Vector2(0.96f, 0.76f));
        stateText = CreateText("State", dialoguePanel.transform, 20f,
            TextAlignmentOptions.BottomLeft, new Color(0.72f, 0.78f, 0.84f));
        SetAnchors(stateText.rectTransform, new Vector2(0.04f, 0.26f), new Vector2(0.96f, 0.38f));
        hintText = CreateText("Hints", dialoguePanel.transform, 18f,
            TextAlignmentOptions.BottomRight, new Color(0.65f, 0.7f, 0.76f));
        SetAnchors(hintText.rectTransform, new Vector2(0.55f, 0.04f), new Vector2(0.96f, 0.2f));
        input = CreateInputField(dialoguePanel.transform);
        SetAnchors(input.GetComponent<RectTransform>(), new Vector2(0.04f, 0.05f), new Vector2(0.54f, 0.23f));
        dialoguePanel.SetActive(false);
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, float size,
        TextAlignmentOptions alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = NpcDialogueUiResources.RuntimeFont;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static TMP_InputField CreateInputField(Transform parent)
    {
        var root = new GameObject("Question Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        root.transform.SetParent(parent, false);
        root.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.2f, 1f);
        var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(root.transform, false);
        SetAnchors(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(14f, 5f), new Vector2(-14f, -5f));
        var text = CreateText("Text", viewport.transform, 24f, TextAlignmentOptions.MidlineLeft, Color.white);
        SetAnchors(text.rectTransform, Vector2.zero, Vector2.one);
        var placeholder = CreateText("Placeholder", viewport.transform, 24f,
            TextAlignmentOptions.MidlineLeft, new Color(1f, 1f, 1f, 0.45f));
        placeholder.text = "Type a question...";
        SetAnchors(placeholder.rectTransform, Vector2.zero, Vector2.one);
        var field = root.GetComponent<TMP_InputField>();
        field.textViewport = viewport.GetComponent<RectTransform>();
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 240;
        return field;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max,
        Vector2? offsetMin = null, Vector2? offsetMax = null)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin ?? Vector2.zero;
        rect.offsetMax = offsetMax ?? Vector2.zero;
    }
}
