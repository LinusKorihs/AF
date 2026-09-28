using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class LocalNpcSseResponseHandler : DownloadHandlerScript
{
    [Serializable] private sealed class Chunk { public Choice[] choices; public Usage usage; }
    [Serializable] private sealed class Choice { public Delta delta; }
    [Serializable] private sealed class Delta { public string content; }
    [Serializable] private sealed class Usage { public int completion_tokens; }

    private readonly Stopwatch timer;
    private readonly List<byte> pendingLine = new List<byte>();
    private readonly StringBuilder content = new StringBuilder();

    public LocalNpcSseResponseHandler(Stopwatch timer) : base(new byte[4096]) => this.timer = timer;
    public string Content => content.ToString();
    public double? FirstContentMilliseconds { get; private set; }
    public double? LastContentMilliseconds { get; private set; }
    public int OutputTokens { get; private set; }
    public string ParseError { get; private set; }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        for (int i = 0; i < dataLength; i++)
        {
            if (data[i] == '\n')
            {
                ProcessLine();
                pendingLine.Clear();
            }
            else pendingLine.Add(data[i]);
        }
        return true;
    }

    protected override void CompleteContent()
    {
        if (pendingLine.Count > 0) ProcessLine();
    }

    private void ProcessLine()
    {
        if (pendingLine.Count == 0) return;
        string line = Encoding.UTF8.GetString(pendingLine.ToArray()).TrimEnd('\r');
        if (!line.StartsWith("data:", StringComparison.Ordinal)) return;
        string payload = line.Substring(5).Trim();
        if (payload.Length == 0 || payload == "[DONE]") return;
        try
        {
            var chunk = JsonUtility.FromJson<Chunk>(payload);
            if (chunk?.usage != null && chunk.usage.completion_tokens > 0)
                OutputTokens = chunk.usage.completion_tokens;
            if (chunk?.choices == null) return;
            foreach (var choice in chunk.choices)
            {
                string part = choice?.delta?.content;
                if (string.IsNullOrEmpty(part)) continue;
                if (!FirstContentMilliseconds.HasValue)
                    FirstContentMilliseconds = timer.Elapsed.TotalMilliseconds;
                LastContentMilliseconds = timer.Elapsed.TotalMilliseconds;
                content.Append(part);
            }
        }
        catch (Exception ex) { ParseError = ex.Message; }
    }
}
