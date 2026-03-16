using UnityEngine;
// using TMPro;

public class GameFlowManager : MonoBehaviour
{
    public MotherboardState motherboardState;
    public MotherboardPlacer motherboardPlacer;
    // public TMP_Text hintText;

    void Start()
    {
        UpdateUI();
        if (motherboardState != null)
        {
            motherboardState.onRequirementsMet.AddListener(UpdateUI);
            motherboardState.onRequirementsUnmet.AddListener(UpdateUI);
        }
    }

    void UpdateUI()
    {
        bool ok = motherboardState != null && motherboardState.HasRequiredMemory;

        // if (hintText != null)
        //     hintText.text = ok
        //         ? "Agora posicione a placa-mãe no gabinete."
        //         : "Instale a memória RAM na placa-mãe primeiro.";

        if (motherboardPlacer != null)
            motherboardPlacer.enabled = true; // deixamos ativo sempre, o gate está dentro dele

        Debug.Log(ok
            ? "[Fluxo] RAM ok. Pode encaixar a placa-mãe."
            : "[Fluxo] Instale a RAM antes de encaixar a placa-mãe.");
    }
}