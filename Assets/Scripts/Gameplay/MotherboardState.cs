using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MotherboardState : MonoBehaviour
{
    [Header("Requisitos")]
    [Tooltip("Quantos módulos de RAM são obrigatórios antes de permitir instalar a placa-mãe.")]
    public int requiredRamCount = 1;

    [Header("Estado (somente leitura)")]
    [SerializeField] private List<RAMModule> installed = new List<RAMModule>();

    [Header("Eventos")]
    public UnityEvent onRequirementsMet;
    public UnityEvent onRequirementsUnmet;

    public bool HasRequiredMemory => installed.Count >= requiredRamCount;

    public void RegisterRam(RAMModule ram)
    {
        if (ram == null) return;
        if (!installed.Contains(ram))
        {
            installed.Add(ram);
            Debug.Log($"[MotherboardState] ✅ RegisterRam '{ram.name}'. installed={installed.Count}/{requiredRamCount}");
            if (HasRequiredMemory) onRequirementsMet?.Invoke();
        }
    }

    public void UnregisterRam(RAMModule ram)
    {
        if (ram == null) return;
        if (installed.Remove(ram))
        {
            Debug.Log($"[MotherboardState] ❌ UnregisterRam '{ram.name}'. installed={installed.Count}/{requiredRamCount}");
            if (!HasRequiredMemory) onRequirementsUnmet?.Invoke();
        }
    }
}