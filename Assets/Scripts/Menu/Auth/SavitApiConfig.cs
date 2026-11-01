using UnityEngine;

/// <summary>
/// Endereços do backend do SAVIT (login, runs e ranking) e do site de cadastro.
/// O app não fala direto com o banco: quem acessa o Supabase é o backend.
/// </summary>
[CreateAssetMenu(fileName = "SavitApiConfig", menuName = "SAVIT/Api Config")]
public class SavitApiConfig : ScriptableObject
{
    [Tooltip("Base do backend (API Gateway), sem barra no fim. Rotas: /auth, /runs, /runs/ranking, /runs/user/:userId.")]
    public string baseUrl = "https://ihrws12500.execute-api.us-east-1.amazonaws.com/default";

    [Tooltip("Página do site do SAVIT onde o aluno cria a conta (aberta pelo botão \"Criar conta\").")]
    public string signupUrl = "";

    private static SavitApiConfig cached;

    public bool IsValid => !string.IsNullOrWhiteSpace(baseUrl);

    /// <summary>Carrega Assets/Resources/SavitApiConfig.asset.</summary>
    public static SavitApiConfig Load()
    {
        if (cached == null) cached = Resources.Load<SavitApiConfig>("SavitApiConfig");
        return cached;
    }
}
