using System;

/// <summary>
/// Eventos de montagem usados pela pontuação (RunRecorder): cada peça avisa quando foi solta fora do
/// lugar (erro) e quando terminou de encaixar. Os ids são os mesmos das etapas do tutorial.
/// </summary>
public static class AssemblyEvents
{
    public const string Ram = "ram";
    public const string Motherboard = "motherboard";
    public const string PowerSupply = "psu";
    public const string Atx24 = "atx24";
    public const string HardDrive = "hd";

    /// <summary>Peça solta fora do lugar. heldSeconds = quanto tempo ela ficou na mão.</summary>
    public static event Action<string, float> Missed;

    /// <summary>Peça encaixada (etapa concluída).</summary>
    public static event Action<string> Completed;

    public static void Miss(string stage, float heldSeconds) => Missed?.Invoke(stage, heldSeconds);

    public static void Complete(string stage) => Completed?.Invoke(stage);
}
