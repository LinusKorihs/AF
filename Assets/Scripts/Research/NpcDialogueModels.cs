using System;

public enum LocalNpcUiMode
{
    Player,
    Research
}

public enum NpcDialogueErrorCode
{
    None,
    NotReady,
    Configuration,
    Network,
    Timeout,
    SseParse,
    InvalidJson,
    UnexpectedFields,
    MissingField,
    InvalidValue,
    StateMismatch
}

[Serializable]
public sealed class NpcDialogueRequest
{
    public string RunId = "manual";
    public string Phase = "manual";
    public string CaseId = "";
    public int Repeat;
    public int ModelOrder;
    public string NpcId;
    public string Question;
    public bool DeliveryArrived;
    public string ExpectedState;
    public string OutputPath;
}

public sealed class NpcDialogueValidation
{
    public string Dialogue;
    public string SupplyState;
    public bool SyntaxValid;
    public bool StructureValid;
    public bool StateValid;
    public NpcDialogueErrorCode ErrorCode;
    public string Error;

    public bool IsUsable => SyntaxValid && StructureValid && StateValid
        && ErrorCode == NpcDialogueErrorCode.None;
}

public sealed class NpcDialogueResult
{
    public NpcDialogueRequest Request;
    public string Backend;
    public string Model;
    public string KnowledgeHash;
    public string WorldObjects;
    public string RawResponse;
    public double? FirstTextMilliseconds;
    public double? LastTextMilliseconds;
    public double ValidatedResponseMilliseconds;
    public int OutputTokens;
    public double? ClientOutputTokensPerSecond;
    public NpcDialogueValidation Validation = new NpcDialogueValidation();

    public bool IsUsable => Validation != null && Validation.IsUsable;
}
