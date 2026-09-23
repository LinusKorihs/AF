using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class NpcDialogueCsvLogger
{
    private const string Header =
        "utc,protocol_id,run_id,phase,case_id,repeat,model_order,backend,knowledge_sha256,model,npc,delivery_arrived,expected_state,world_objects,question,temperature,context_tokens,max_output_tokens,first_text_ms,last_text_ms,validated_response_ms,output_tokens,client_output_tokens_per_second,syntax_valid,structure_valid,state_valid,error_code,facts_review,role_review,raw_response,error\n";

    private readonly LocalNpcDialogueSettings settings;
    public NpcDialogueCsvLogger(LocalNpcDialogueSettings settings) => this.settings = settings;

    public void Write(NpcDialogueResult result)
    {
        if (result?.Request == null) return;
        try
        {
            string path = ResolvePath(result.Request.OutputPath);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (!File.Exists(path)) File.WriteAllText(path, Header, new UTF8Encoding(true));

            var request = result.Request;
            var validation = result.Validation ?? new NpcDialogueValidation();
            string[] fields = {
                DateTime.UtcNow.ToString("O"), settings.ProtocolId, request.RunId,
                request.Phase, request.CaseId, request.Repeat.ToString(),
                request.ModelOrder.ToString(), result.Backend, result.KnowledgeHash,
                result.Model, request.NpcId, request.DeliveryArrived.ToString(),
                request.ExpectedState, result.WorldObjects, request.Question,
                settings.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
                settings.ContextTokens.ToString(), settings.MaxOutputTokens.ToString(),
                Number(result.FirstTextMilliseconds), Number(result.LastTextMilliseconds),
                result.ValidatedResponseMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                result.OutputTokens.ToString(), Number(result.ClientOutputTokensPerSecond),
                validation.SyntaxValid.ToString(), validation.StructureValid.ToString(),
                validation.StateValid.ToString(), validation.ErrorCode.ToString(),
                "pending", "pending", result.RawResponse, validation.Error
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
        catch (Exception ex)
        {
            Debug.LogWarning("[NPC Pilot] CSV could not be written: " + ex.Message);
        }
    }

    private string ResolvePath(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return Path.Combine(Application.persistentDataPath, settings.CsvFileName);
        return Path.IsPathRooted(requested)
            ? requested : Path.GetFullPath(Path.Combine(Application.persistentDataPath, requested));
    }

    private static string Number(double? value) => value.HasValue
        ? value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "";
}
