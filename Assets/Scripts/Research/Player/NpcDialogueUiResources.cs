using TMPro;
using UnityEngine;

public static class NpcDialogueUiResources
{
    private static TMP_FontAsset runtimeFont;

    public static TMP_FontAsset RuntimeFont
    {
        get
        {
            if (runtimeFont != null) return runtimeFont;
            runtimeFont = Resources.Load<TMP_FontAsset>(
                "Fonts & Materials/LiberationSans SDF");
            return runtimeFont;
        }
    }
}
