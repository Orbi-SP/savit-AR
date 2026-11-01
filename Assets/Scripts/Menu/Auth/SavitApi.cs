using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Chamadas ao backend do SAVIT (SavitApiConfig.baseUrl). Com withToken, envia o token do login em
/// Authorization: Bearer (exigido por /runs, /runs/ranking e /runs/user/:userId).
/// done recebe o status HTTP (0 = sem conexão) e o corpo da resposta.
/// </summary>
public static class SavitApi
{
    // Lambda pode demorar na primeira chamada (cold start).
    private const int TimeoutSeconds = 25;

    public static bool IsSuccess(long status) => status >= 200 && status < 300;

    public static IEnumerator Send(string method, string path, string json, bool withToken, Action<long, string> done)
    {
        var config = SavitApiConfig.Load();
        if (config == null || !config.IsValid)
        {
            const string error = "SavitApiConfig não está configurado.";
            Debug.LogError("[SavitApi] " + error);
            done?.Invoke(0, error);
            yield break;
        }

        using (var req = Build(config, method, path, json, withToken))
        {
            yield return req.SendWebRequest();
            long status = req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode;
            string body = req.downloadHandler?.text ?? "";
            if (!IsSuccess(status)) Debug.LogWarning($"[SavitApi] {method} {path}: HTTP {status} {req.error} {body}");
            done?.Invoke(status, body);
        }
    }

    public static IEnumerator GetRanking(Action<long, string> done)
    {
        yield return Send("GET", "/runs/ranking", null, true, done);
    }

    /// <summary>Envia sem esperar a resposta (ex.: ao sair da cena, quando não dá mais para rodar corrotina).</summary>
    public static void FireAndForget(string method, string path, string json, bool withToken)
    {
        var config = SavitApiConfig.Load();
        if (config == null || !config.IsValid)
        {
            Debug.LogError("[SavitApi] SavitApiConfig não está configurado.");
            return;
        }

        var req = Build(config, method, path, json, withToken);
        req.SendWebRequest().completed += _ => req.Dispose();
    }

    private static UnityWebRequest Build(SavitApiConfig config, string method, string path, string json, bool withToken)
    {
        var req = new UnityWebRequest(config.baseUrl.TrimEnd('/') + path, method);
        if (json != null) req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.timeout = TimeoutSeconds;
        req.SetRequestHeader("Content-Type", "application/json");
        if (withToken && !string.IsNullOrEmpty(AuthSession.Token))
            req.SetRequestHeader("Authorization", "Bearer " + AuthSession.Token);
        return req;
    }
}
