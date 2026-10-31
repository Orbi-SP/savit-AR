using UnityEngine;

/// <summary>
/// Endereço da API e chave pública (anon/publishable) do projeto Supabase.
/// A chave pública é feita para ir dentro do app: quem protege os dados são as políticas RLS do banco
/// (Tools/supabase/001_login_e_pontuacao.sql). Nunca coloque aqui a senha do Postgres nem a service_role key.
/// </summary>
[CreateAssetMenu(fileName = "SupabaseConfig", menuName = "SAVIT/Supabase Config")]
public class SupabaseConfig : ScriptableObject
{
    [Tooltip("URL da API do projeto (Settings > API no painel do Supabase).")]
    public string url = "https://eyakfzeaqhroiatvomog.supabase.co";

    [Tooltip("Chave pública: \"anon public\" ou \"publishable\" (Settings > API Keys). Nunca a service_role.")]
    [TextArea(2, 6)] public string anonKey = "";

    [Tooltip("Página do site do SAVIT onde o aluno cria a conta (aberta pelo botão \"Criar conta\").")]
    public string signupUrl = "";

    private static SupabaseConfig cached;

    public bool IsValid => !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(anonKey);

    /// <summary>Carrega Assets/Resources/SupabaseConfig.asset.</summary>
    public static SupabaseConfig Load()
    {
        if (cached == null) cached = Resources.Load<SupabaseConfig>("SupabaseConfig");
        return cached;
    }
}
