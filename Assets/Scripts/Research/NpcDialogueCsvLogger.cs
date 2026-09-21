using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class NpcDialogueCsvLogger
{
    public sealed class Entry
    {
        public string Backend, KnowledgeHash, Model, Npc, WorldObjects, Question, Error;
        public bool DeliveryArrived, SyntaxValid, StructureValid, StateValid;
        public double TotalMilliseconds;
        public LocalNpcSseResponseHandler Stream;
    }

    private readonly LocalNpcDialogueSettings settings;
    public NpcDialogueCsvLogger(LocalNpcDialogueSettings settings) => this.settings = settings;

    public void Write(Entry entry)
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, settings.CsvFileName);
            if (!File.Exists(path))
                File.WriteAllText(path,
                    "utc,protocol_id,backend,knowledge_sha256,model,npc,delivery_arrived,world_objects,question,first_text_ms,last_text_ms,total_ms,output_tokens,syntax_valid,structure_valid,state_valid,raw_response,error\n",
                    new UTF8Encoding(true));
            string[] fields = {
                DateTime.UtcNow.ToString("O"), settings.ProtocolId, entry.Backend, entry.KnowledgeHash,
                entry.Model, entry.Npc, entry.DeliveryArrived.ToString(), entry.WorldObjects, entry.Question,
                Number(entry.Stream.FirstContentMilliseconds), Number(entry.Stream.LastContentMilliseconds),
                entry.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                entry.Stream.OutputTokens.ToString(), entry.SyntaxValid.ToString(),
                entry.StructureValid.ToString(), entry.StateValid.ToString(), entry.Stream.Content, entry.Error
            };
            var line = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) line.Append(',');
                line.Append('"').Append((fields[i] ?? "").Replace("\"", "\"\"")).Append('"');
            }
            File.AppendAllText(path, line.Append('\n').ToString(), Encoding.UTF8);
            Debug.Log("[NPC Pilot] CSV: " + path);
        }
        catch (Exception ex) { Debug.LogWarning("[NPC Pilot] CSV could not be written: " + ex.Message); }
    }

    private static string Number(double? value) => value.HasValue
        ? value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "";
}
