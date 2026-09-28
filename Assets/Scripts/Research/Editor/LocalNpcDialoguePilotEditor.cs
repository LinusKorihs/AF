using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LocalNpcDialoguePilot))]
[CanEditMultipleObjects]
public sealed class LocalNpcDialoguePilotEditor : Editor
{
    private Editor catalogEditor;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var settingsProperty = serializedObject.FindProperty("settings");
        var modelProperty = serializedObject.FindProperty("startupModel");

        EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(settingsProperty, new GUIContent("Settings",
            "Settings asset containing model choices, inference parameters, and logging options."));
        var settings = settingsProperty.objectReferenceValue as LocalNpcDialogueSettings;
        var options = settings?.Models;
        if (options != null && options.Length > 0)
        {
            var names = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
                names[i] = options[i]?.label ?? "(unconfigured)";
            modelProperty.intValue = EditorGUILayout.Popup(new GUIContent("Startup Model",
                    "Model loaded at scene start when automatic loading is enabled."),
                Mathf.Clamp(modelProperty.intValue, 0, options.Length - 1), names);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(new GUIContent("GGUF File",
                    "Filename of the selected model in the LocalModels folder."),
                    options[modelProperty.intValue]?.fileName ?? "");
        }
        else EditorGUILayout.HelpBox("Assign a settings asset with at least one model.", MessageType.Warning);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Information", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("question"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("knowledgeJson"),
            new GUIContent("Knowledge",
                "JSON file containing world facts, NPC roles, knowledge boundaries, and response instructions."));
        var catalogProperty = serializedObject.FindProperty("testCatalog");
        EditorGUILayout.PropertyField(catalogProperty,
            new GUIContent("Test Catalog",
                "Versioned catalog used by the automated Research Mode comparison."));
        var catalog = catalogProperty.objectReferenceValue as NpcDialogueTestCatalogAsset;
        if (catalog != null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Test Catalog Contents", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                Editor.CreateCachedEditor(catalog, null, ref catalogEditor);
                catalogEditor.OnInspectorGUI();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Modes", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("mode"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("loadModelOnSceneStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("npcKeys"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("deliveryKey"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("batchRepeats"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("includeCpuInBatch"));
        serializedObject.ApplyModifiedProperties();
    }
}
