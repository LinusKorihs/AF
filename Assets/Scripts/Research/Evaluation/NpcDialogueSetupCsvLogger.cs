using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class NpcDialogueSetupCsvLogger
{
    private const string Header =
        "utc,protocol_id,run_id,backend_order,model_order,backend,model,server_ready_ms,warmup_ms,total_startup_ms,success,error\n";

    private readonly LocalNpcDialogueSettings settings;

    public NpcDialogueSetupCsvLogger(LocalNpcDialogueSettings settings)
    {
        this.settings = settings;
    }

    public void Write(NpcModelSetupResult result)
    {
        if (result == null) return;
        try
        {
            string path = ResolvePath(result.OutputPath);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (!File.Exists(path)) File.WriteAllText(path, Header, new UTF8Encoding(true));

            string[] fields = {
                DateTime.UtcNow.ToString("O"), settings.ProtocolId, result.RunId,
                result.BackendOrder.ToString(), result.ModelOrder.ToString(), result.Backend,
                result.Model, Number(result.ServerReadyMilliseconds), Number(result.WarmupMilliseconds),
                result.TotalStartupMilliseconds.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture),
                result.Success.ToString(), result.Error
            };
            var line = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) line.Append(',');
                line.Append('"').Append((fields[i] ?? "").Replace("\"", "\"\"")).Append('"');
            }
            File.AppendAllText(path, line.Append('\n').ToString(), Encoding.UTF8);
            Debug.Log("[NPC Pilot] Setup CSV: " + path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[NPC Pilot] Setup CSV could not be written: " + ex.Message);
        }
    }

    private string ResolvePath(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return Path.Combine(Application.persistentDataPath, settings.SetupCsvFileName);
        string responsePath = Path.IsPathRooted(requested)
            ? requested : Path.GetFullPath(Path.Combine(Application.persistentDataPath, requested));
        string directory = Path.GetDirectoryName(responsePath);
        string name = Path.GetFileNameWithoutExtension(responsePath) + "_setup.csv";
        return Path.Combine(directory ?? Application.persistentDataPath, name);
    }

    private static string Number(double? value) => value.HasValue
        ? value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "";
}
