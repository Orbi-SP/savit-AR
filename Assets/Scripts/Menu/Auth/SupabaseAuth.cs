using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Usuário logado, válido no jogo todo. A pontuação (próxima etapa) grava com AccessToken + UserId.
/// </summary>
public static class AuthSession
{
    public static string UserId { get; private set; }
    public static string Email { get; private set; }
    public static string AccessToken { get; private set; }
    public static DateTime ExpiresAtUtc { get; private set; }

    public static bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

    internal static void Set(string userId, string email, string accessToken, int expiresInSeconds)
    {
        UserId = userId;
        Email = email;
        AccessToken = accessToken;
        ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);
    }

    internal static void Clear()
    {
        UserId = Email = AccessToken = null;
        ExpiresAtUtc = DateTime.MinValue;
    }
}

/// <summary>
/// Login por e-mail + senha no Supabase Auth (API REST, sem SDK). A conta é criada no site do SAVIT com o
/// e-mail do próprio aluno; o app só faz login, enviando o e-mail como foi digitado.
/// O refresh token fica salvo no aparelho para entrar direto na próxima vez.
/// </summary>
public static class SupabaseAuth
{
    private const string RefreshTokenKey = "savit.auth.refresh_token";
    private const int TimeoutSeconds = 15;

#pragma warning disable 0649 // respostas preenchidas pelo JsonUtility
    [Serializable] private class PasswordBody { public string email; public string password; }
    [Serializable] private class RefreshBody { public string refresh_token; }
    [Serializable] private class UserInfo { public string id; public string email; }
    [Serializable] private class SessionResponse { public string access_token; public string refresh_token; public int expires_in; public UserInfo user; }
    [Serializable] private class ErrorResponse { public string error_code; public string msg; public string message; public string error; public string error_description; }
#pragma warning restore 0649

    public static IEnumerator SignIn(string email, string password, Action<bool, string> done)
    {
        // E-mail como foi digitado; só tira espaços das pontas (o teclado do celular costuma pôr um no fim).
        string mail = (email ?? "").Trim();
        if (!Validate(mail, password, out string error)) { done(false, error); yield break; }

        var body = new PasswordBody { email = mail, password = password };
        yield return Post("/auth/v1/token?grant_type=password", JsonUtility.ToJson(body), null, (ok, json) =>
        {
            if (!ok) { done(false, Explain(json)); return; }
            done(StartSession(json), null);
        });
    }

    /// <summary>Entra de novo com o refresh token salvo (login automático ao abrir o app).</summary>
    public static IEnumerator TryRestore(Action<bool> done)
    {
        string refresh = PlayerPrefs.GetString(RefreshTokenKey, "");
        var config = SupabaseConfig.Load();
        if (string.IsNullOrEmpty(refresh) || config == null || !config.IsValid) { done(false); yield break; }

        var body = new RefreshBody { refresh_token = refresh };
        yield return Post("/auth/v1/token?grant_type=refresh_token", JsonUtility.ToJson(body), null, (ok, json) =>
        {
            if (!ok || !StartSession(json))
            {
                Forget();
                done(false);
                return;
            }
            done(true);
        });
    }

    public static IEnumerator SignOut(Action done)
    {
        string token = AuthSession.AccessToken;
        AuthSession.Clear();
        Forget();
        if (!string.IsNullOrEmpty(token))
            yield return Post("/auth/v1/logout", "{}", token, (_, __) => { });
        done?.Invoke();
    }

    private static bool Validate(string email, string password, out string error)
    {
        error = null;
        var config = SupabaseConfig.Load();
        if (config == null || !config.IsValid)
            error = "Login não configurado: preencha a chave pública em Assets/Resources/SupabaseConfig.";
        else if (email.Length == 0 || email.IndexOf('@') <= 0)
            error = "Digite o e-mail usado para criar a conta no site do SAVIT.";
        else if (string.IsNullOrEmpty(password))
            error = "Digite a senha.";
        return error == null;
    }

    private static bool StartSession(string json)
    {
        SessionResponse session;
        try { session = JsonUtility.FromJson<SessionResponse>(json); }
        catch (Exception) { return false; }
        if (session == null || string.IsNullOrEmpty(session.access_token) || session.user == null) return false;

        AuthSession.Set(session.user.id, session.user.email, session.access_token, session.expires_in);
        PlayerPrefs.SetString(RefreshTokenKey, session.refresh_token ?? "");
        PlayerPrefs.Save();
        return true;
    }

    private static void Forget()
    {
        PlayerPrefs.DeleteKey(RefreshTokenKey);
        PlayerPrefs.Save();
    }

    private static IEnumerator Post(string path, string json, string bearer, Action<bool, string> done)
    {
        var config = SupabaseConfig.Load();
        using (var req = new UnityWebRequest(config.url.TrimEnd('/') + path, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = TimeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("apikey", config.anonKey);
            if (!string.IsNullOrEmpty(bearer)) req.SetRequestHeader("Authorization", "Bearer " + bearer);

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                done(false, "{\"error_code\":\"network\"}");
                yield break;
            }
            done(req.result == UnityWebRequest.Result.Success, req.downloadHandler.text);
        }
    }

    /// <summary>Mensagem em português para os erros mais comuns do Supabase Auth.</summary>
    private static string Explain(string json)
    {
        ErrorResponse e = null;
        try { e = JsonUtility.FromJson<ErrorResponse>(json); } catch (Exception) { }
        string code = e?.error_code ?? e?.error ?? "";

        switch (code)
        {
            case "network": return "Sem conexão com a internet. Verifique e tente de novo.";
            case "invalid_credentials":
            case "invalid_grant": return "E-mail ou senha incorretos. Ainda não tem conta? Toque em Criar conta.";
            case "email_not_confirmed": return "Confirme seu e-mail pelo link enviado quando a conta foi criada.";
            case "email_provider_disabled": return "Login por e-mail desativado no Supabase (Authentication > Sign In / Providers > Email).";
            case "over_request_rate_limit": return "Muitas tentativas. Aguarde um pouco e tente de novo.";
        }

        Debug.LogWarning("[SupabaseAuth] Erro não mapeado: " + json);
        return "Não foi possível entrar agora. Tente de novo.";
    }
}
