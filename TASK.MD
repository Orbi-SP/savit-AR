# Task: Implementação da Tela de Ranking (Global e Pessoal)

## 📌 Objetivo
Criar uma interface de Ranking no Menu Principal, acessível após o login. A tela deve abrir como um componente sobreposto (overlay) e exibir duas visões que o usuário pode alternar: **Ranking Global** e **Meu Ranking**. 

## 🎨 Requisitos de Interface (UI/UX)
- **Abertura da Tela:** Deve ser instanciada/ativada por cima da cena atual do Menu.
- **Botões Superiores (Navegação):**
  - `Voltar ao Menu`: Fecha a tela de ranking e retorna ao menu inicial.
  - `Meu Ranking`: Alterna para a lista contendo apenas o histórico do jogador logado.
  - `Ranking Global`: Alterna para a lista com as melhores pontuações de todos (Visão Padrão).
- **Layout da Lista - Ranking Global:**
  - Posição do jogador à esquerda (Ex: `1º`, `2º`, `3º`...).
  - Nome do jogador (`username`).
  - Pontuação Total (`total_points`).
  - Tempo gasto formatado: `Xm Yseg` (Ex: `1m 10seg`, `0min 50seg`).
- **Layout da Lista - Meu Ranking:**
  - Ordenação visual da partida mais recente para a mais antiga.
  - Nome do jogador.
  - Pontuação Total.
  - Tempo gasto formatado: `Xm Yseg`.

## ⚙️ Regras de Negócio e Cálculo de Tempo no Unity
- **Desempate (Global):** O backend já retorna a lista ordenada corretamente (maior ponto e, em caso de empate, menor tempo). O Front-end (C#) precisa apenas iterar no array recebido e desenhar os itens na tela adicionando os números da posição (`1º, 2º...`).
- **Formatação de Tempo (C#):**
  - Na rota **Global**, o tempo já vem calculado no campo `time_spent_ms`.
  - Na rota **Pessoal**, o backend retorna `started_at` e `finished_at`. O Unity precisará fazer o parse dessas datas (ISO 8601) e calcular a diferença.
  - *Sugestão de formatação no C#:*
    ```csharp
    TimeSpan time = TimeSpan.FromMilliseconds(timeSpentMs); 
    // ou TimeSpan time = finishedAt - startedAt;
    string tempoFormatado = $"{time.Minutes}m {time.Seconds}seg";
    ```

## 🔌 Integração com a API
A comunicação deve usar a classe `SavitApiConfig` para resgatar a `baseUrl`. Ambas as rotas exigem autenticação enviando o Header: `Authorization: Bearer <TOKEN>`. A estrutura deve seguir a mesma lógica de autorização já implementada na criação de run.

### 1. Rota: Ranking Global
- **Método:** `GET`
- **Endpoint:** `/runs/ranking`
- **Retorno Esperado:**
  ```json
  [
    {
      "run_id": 1,
      "username": "Luiz",
      "total_points": 1500,
      "time_spent_ms": 70000,
      "time_spent_seconds": 70
    }
  ]

  2. Rota: Meu Ranking
Método: GET

Endpoint: /runs/user/:userId (Substituir :userId pelo ID do usuário logado salvo no AuthManager)

Retorno Esperado:

JSON
[
  {
    "id": 15,
    "started_at": "2026-11-01T15:00:00.000Z",
    "finished_at": "2026-11-01T15:01:10.000Z",
    "status": "finished",
    "total_points": 1500,
    "total_errors": 0,
    "scores": [...]
  }
]
✅ Critérios de Aceite
[ ] A tela de ranking sobrepõe o menu principal corretamente.

[ ] Requisições GET feitas via UnityWebRequest passando o Bearer Token no Header.

[ ] O Ranking Global exibe as posições no canto esquerdo (1º, 2º, etc.).

[ ] O tempo é exibido formatado (Ex: 1m 10seg) perfeitamente nas duas abas.

[ ] O botão de alternar abas recarrega ou altera as listas na UI sem quebrar o layout.

[ ] O botão "Voltar" esconde a UI de ranking adequadamente.