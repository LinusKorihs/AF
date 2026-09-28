using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Research/Local NPC Dialogue Settings")]
public sealed class LocalNpcDialogueSettings : ScriptableObject
{
    [Serializable]
    public sealed class ModelOption
    {
        [Tooltip("Model alias used by llama.cpp and sent with each chat request. Keep it unique within this list.")]
        public string id;
        [Tooltip("Name shown on the model button in the dialogue panel.")]
        public string label;
        [Tooltip("GGUF filename expected in the LocalModels folder next to the Unity project or game executable.")]
        public string fileName;
    }

    [Header("Models")]
    [Tooltip("Models available for loading and switching in the dialogue panel.")]
    [SerializeField] private ModelOption[] models = {
        new ModelOption { id = "qwen3-4b-instruct-2507", label = "Qwen 3 4B",
            fileName = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf" },
        new ModelOption { id = "gemma-3-4b-it", label = "Gemma 3 4B",
            fileName = "gemma-3-4b-it-Q4_K_M.gguf" }
    };

    [Header("Inference")]
    [Tooltip("Local TCP port used by the bundled llama.cpp server. The port must be free when loading a model.")]
    [SerializeField, Min(1)] private int port = 8080;
    [Tooltip("Context window in tokens passed to llama.cpp when the server starts.")]
    [SerializeField, Min(128)] private int contextTokens = 2048;
    [Tooltip("Maximum number of tokens generated for one NPC reply.")]
    [SerializeField, Min(1)] private int maxOutputTokens = 160;
    [Tooltip("Maximum output tokens for the startup warmup request; warmup is excluded from dialogue timing.")]
    [SerializeField, Min(1)] private int warmupOutputTokens = 1;
    [Tooltip("Sampling temperature for NPC replies. Lower values usually make responses less variable.")]
    [SerializeField, Range(0f, 2f)] private float temperature = 0.2f;
    [Tooltip("Number of concurrent request slots configured when llama.cpp starts.")]
    [SerializeField, Min(1)] private int parallelRequests = 1;
    [Tooltip("Maximum time in seconds for one dialogue request before it is logged as a timeout.")]
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 120;
    [Tooltip("Question sent during startup warmup before the model is marked ready.")]
    [SerializeField] private string warmupQuestion = "Ready?";
    [Tooltip("Base used to derive reproducible per-case sampling seeds for measured batch runs.")]
    [SerializeField] private int batchSeedBase = 20260924;

    [Header("World and logging")]
    [Tooltip("Fact ID of the delivery object in the scene and the matching entry in the knowledge JSON.")]
    [SerializeField] private string deliveryFactId = "supply_delivery";
    [Tooltip("CSV filename written under Application.persistentDataPath for Unity dialogue runs.")]
    [SerializeField] private string csvFileName = "npc_pilot_v9.csv";
    [Tooltip("CSV filename used for model startup and warmup measurements.")]
    [SerializeField] private string setupCsvFileName = "npc_pilot_v9_setup.csv";
    [Tooltip("Protocol identifier stored in each CSV row. Change it when the test protocol changes.")]
    [SerializeField] private string protocolId = "scene-json-v9";

    public ModelOption[] Models => models;
    public int Port => port;
    public int ContextTokens => contextTokens;
    public int MaxOutputTokens => maxOutputTokens;
    public int WarmupOutputTokens => warmupOutputTokens;
    public float Temperature => temperature;
    public int ParallelRequests => parallelRequests;
    public int RequestTimeoutSeconds => requestTimeoutSeconds;
    public string WarmupQuestion => warmupQuestion;
    public int BatchSeedBase => batchSeedBase;
    public string DeliveryFactId => deliveryFactId;
    public string CsvFileName => csvFileName;
    public string SetupCsvFileName => setupCsvFileName;
    public string ProtocolId => protocolId;
    public string BaseUrl => "http://127.0.0.1:" + port;
    public string ChatEndpoint => BaseUrl + "/v1/chat/completions";

    public ModelOption GetModel(int index)
    {
        return models != null && index >= 0 && index < models.Length ? models[index] : null;
    }
}
