using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Aluno logado, válido no jogo todo. As runs e o ranking usam UserId + Token.
/// </summary>
public static class AuthSession
{
    public static string UserId { get; private set; }
    public static string Username { get; private set; }
    public static string Email { get; private set; }
    public static string Token { get; private set; }

    public static bool IsLoggedIn => !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(UserId);

    /// <summary>Nome para mostrar: usuário do cadastro ou, sem ele, o e-mail.</summary>
    public static string DisplayName => string.IsNullOrEmpty(Username) ? Email : Username;

    internal static void Set(string userId, string username, string email, string token)
    {
        UserId = userId;
        Username = username;
        Email = email;
        Token = token;
    }

    internal static void Clear()
    {
        UserId = Username = Email = Token = null;
    }
}

/// <summary>
/// Login no backend do SAVIT (POST /auth com e-mail + senha). A conta é criada no site; o app só entra.
/// O token e o perfil ficam salvos no aparelho para entrar direto na próxima vez.
/// </summary>
public static class SavitAuth
{
    private const string TokenKey = "savit.auth.token";
    private const string UserIdKey = "savit.auth.user_id";
    private const string UsernameKey = "savit.auth.username";
    private const string EmailKey = "savit.auth.email";

#pragma warning disable 0649 // preenchidos pelo JsonUtility
    [Serializable] private class LoginBody { public string email; public string password; }
    [Serializable] private class SignUpBody { public string username; public string email; public string password; }
    [Serializable] private class Profile { public string id; public string username; public string email; }
    [Serializable] private class LoginResponse
    {
        public string token; public string accessToken; public string access_token;
        public Profile profile; public Profile user;
    }
    [Serializable] private class ErrorResponse { public string error; public string message; }
#pragma warning restore 0649

    public static IEnumerator SignIn(string email, string password, Action<bool, string> done)
    {
        // E-mail como foi digitado; só tira espaços das pontas (o teclado do celular costuma pôr um no fim).
        string mail = (email ?? "").Trim();
        var config = SavitApiConfig.Load();
        if (config == null || !config.IsValid) { done(false, "Login não configurado: preencha Assets/Resources/SavitApiConfig."); yield break; }
        if (mail.IndexOf('@') <= 0) { done(false, "Digite um e-mail válido."); yield break; }
        if (string.IsNullOrEmpty(password)) { done(false, "Digite a senha."); yield break; }

        string body = JsonUtility.ToJson(new LoginBody { email = mail, password = password });
        yield return SavitApi.Send("POST", "/auth", body, false, (status, json) =>
        {
            if (status == 0) { done(false, "Sem conexão com a internet. Verifique e tente de novo."); return; }
            if (status == 401) { done(false, "E-mail ou senha incorretos. Ainda não tem conta? Toque em Criar conta."); return; }
            if (!SavitApi.IsSuccess(status)) { done(false, ErrorText(json) ?? "Não foi possível entrar agora. Tente de novo."); return; }

            LoginResponse res = null;
            try { res = JsonUtility.FromJson<LoginResponse>(json); } catch (Exception) { }
            string token = FirstFilled(res?.token, res?.accessToken, res?.access_token);
            Profile profile = res?.profile ?? res?.user ?? new Profile();
            // id numérico (bigint) não entra em campo string no JsonUtility: lê direto do JSON.
            if (string.IsNullOrEmpty(profile.id)) profile.id = IdFromJson(json);
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(profile.id))
            {
                Debug.LogWarning("[SavitAuth] Resposta do /auth sem token ou sem perfil: " + json);
                done(false, "O servidor não devolveu o acesso. Tente de novo mais tarde.");
                return;
            }

            AuthSession.Set(profile.id, profile.username, FirstFilled(profile.email, mail), token);
            Save();
            done(true, null);
        });
    }

    public static IEnumerator CreateUser(string username, string email, string password, Action<bool, string> done)
    {
        string name = (username ?? "").Trim();
        string mail = (email ?? "").Trim();
        var config = SavitApiConfig.Load();
        if (config == null || !config.IsValid) { done(false, "Cadastro não configurado: preencha Assets/Resources/SavitApiConfig."); yield break; }
        if (string.IsNullOrEmpty(name)) { done(false, "Digite um nome de usuário."); yield break; }
        if (mail.IndexOf('@') <= 0) { done(false, "Digite um e-mail válido."); yield break; }
        if (string.IsNullOrEmpty(password)) { done(false, "Digite a senha."); yield break; }

        string body = JsonUtility.ToJson(new SignUpBody { username = name, email = mail, password = password });
        yield return SavitApi.Send("POST", "/user", body, false, (status, json) =>
        {
            if (status == 0) { done(false, "Sem conexão com a internet. Verifique e tente de novo."); return; }
            if (!SavitApi.IsSuccess(status)) { done(false, ErrorText(json) ?? "Não foi possível criar o usuário."); return; }
            done(true, null);
        });
    }

    /// <summary>
    /// Login automático com o token salvo, confirmado no backend (GET /runs/user/:userId).
    /// Token recusado: sai. Sem internet: mantém a sessão (o aluno ainda pode jogar).
    /// </summary>
    public static IEnumerator TryRestore(Action<bool> done)
    {
        string token = PlayerPrefs.GetString(TokenKey, "");
        string userId = PlayerPrefs.GetString(UserIdKey, "");
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId)) { done(false); yield break; }

        AuthSession.Set(userId, PlayerPrefs.GetString(UsernameKey, ""), PlayerPrefs.GetString(EmailKey, ""), token);
        yield return SavitApi.Send("GET", "/runs/user/" + Uri.EscapeDataString(userId), null, true, (status, _) =>
        {
            if (status == 401 || status == 403)
            {
                SignOut();
                done(false);
                return;
            }
            done(true);
        });
    }

    /// <summary>O backend não tem rota de logout: basta esquecer o token no aparelho.</summary>
    public static void SignOut()
    {
        AuthSession.Clear();
        PlayerPrefs.DeleteKey(TokenKey);
        PlayerPrefs.DeleteKey(UserIdKey);
        PlayerPrefs.DeleteKey(UsernameKey);
        PlayerPrefs.DeleteKey(EmailKey);
        PlayerPrefs.Save();
    }

    private static void Save()
    {
        PlayerPrefs.SetString(TokenKey, AuthSession.Token ?? "");
        PlayerPrefs.SetString(UserIdKey, AuthSession.UserId ?? "");
        PlayerPrefs.SetString(UsernameKey, AuthSession.Username ?? "");
        PlayerPrefs.SetString(EmailKey, AuthSession.Email ?? "");
        PlayerPrefs.Save();
    }

    private static string ErrorText(string json)
    {
        try
        {
            var e = JsonUtility.FromJson<ErrorResponse>(json);
            return FirstFilled(e?.error, e?.message);
        }
        catch (Exception) { return null; }
    }

    private static string IdFromJson(string json)
    {
        var m = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"(?:profile|user)\"\\s*:\\s*\\{[^}]*?\"id\"\\s*:\\s*\"?([^\",}\\s]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string FirstFilled(params string[] values)
    {
        foreach (var v in values) if (!string.IsNullOrEmpty(v)) return v;
        return null;
    }
}
