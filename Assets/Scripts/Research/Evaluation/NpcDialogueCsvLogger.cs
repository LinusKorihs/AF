using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class NpcDialogueCsvLogger
{
    private const string Header =
        "utc,protocol_id,run_id,phase,case_id,repeat,backend_order,model_order,backend,catalog_id,catalog_sha256,knowledge_sha256,model,npc,delivery_arrived,expected_state,expectation,review_notes,sampling_seed,world_objects,question,temperature,context_tokens,max_output_tokens,first_text_ms,last_text_ms,validated_response_ms,output_tokens,client_output_tokens_per_second,syntax_valid,structure_valid,state_valid,error_code,fallback_used,fallback_reason,presented_dialogue,facts_review,role_review,knowledge_boundary_review,relevance_review,dialogue_state_review,raw_response,error\n";

    public static string HeaderLine => Header;

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
                request.BackendOrder.ToString(), request.ModelOrder.ToString(),
                result.Backend, request.CatalogId, request.CatalogHash, result.KnowledgeHash,
                result.Model, request.NpcId, request.DeliveryArrived.ToString(),
                request.ExpectedState, request.Expectation, request.ReviewNotes,
                request.SamplingSeed.ToString(), result.WorldObjects, request.Question,
                settings.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
                settings.ContextTokens.ToString(), settings.MaxOutputTokens.ToString(),
                Number(result.FirstTextMilliseconds), Number(result.LastTextMilliseconds),
                result.ValidatedResponseMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                result.OutputTokens.ToString(), Number(result.ClientOutputTokensPerSecond),
                validation.SyntaxValid.ToString(), validation.StructureValid.ToString(),
                validation.StateValid.ToString(), validation.ErrorCode.ToString(),
                result.FallbackUsed.ToString(), result.FallbackReason,
                result.PresentationDialogue, "pending", "pending", "pending", "pending", "pending",
                result.RawResponse, validation.Error
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
