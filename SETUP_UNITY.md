# 🎮 Configuração do SAVIT - Unity 6000.2.6f2

Este guia descreve o fluxo atual do menu, autenticação e gravação de pontuação. O backend fica fora deste repositório: suas funções AWS Lambda recebem as requisições do aplicativo, validam o JWT e usam o Supabase para persistência.

## Configuração da API

O projeto já contém `Assets/Resources/SavitApiConfig.asset`.

1. Abra `Assets/Resources/SavitApiConfig.asset` no Inspector.
2. Em **Base Url**, mantenha `https://ihrws12500.execute-api.us-east-1.amazonaws.com/default`.
3. Não crie outro asset com o mesmo nome em `Resources`; os scripts carregam este asset automaticamente.
4. Não coloque chaves privadas, `service_role` ou credenciais do Supabase no Unity.

## Fluxo atual do menu

1. Abra `Assets/Scenes/MainMenu.unity`.
2. O `MenuController` adiciona o `LoginController` automaticamente ao objeto do menu.
3. O login é montado por código sobre o Canvas existente; não é necessário criar ou remover objetos da cena.
4. O menu permanece em retrato.
5. **Entrar** chama `POST /auth` com e-mail e senha. O JWT, `user_id`, usuário e e-mail são mantidos em memória e salvos em `PlayerPrefs`.
6. **Criar conta** chama `POST /user` diretamente, usando o prefixo do e-mail como `username`; depois da criação, o aplicativo faz login automaticamente.
7. **Jogar** só funciona com sessão válida e força a orientação paisagem antes de carregar `BRP Sample SceneGabinete`.

## Fluxo de runs, pontuação e ranking

1. Na cena de gameplay, o `RunRecorder` acompanha os eventos de encaixe publicados por `AssemblyEvents`.
2. Cada componente concluído gera um item em `scoresData`: 100 pontos menos 10 por erro, com mínimo zero.
3. Ao concluir o tutorial, a run é enviada com status `finished`. Se o jogador sair antes, ela é enviada com status `abandoned`.
4. O envio é autenticado com `Authorization: Bearer <JWT>` para `POST /runs`.
5. O corpo enviado contém `runData` com `user_id`, timestamps UTC em ISO 8601, status, totais e versão do aplicativo, além de `scoresData` com `points` e `stage`.
6. A Lambda associa a run ao usuário autenticado e grava a run e os scores no Supabase. O cliente Unity não acessa o banco diretamente.
7. O ranking deve ser consultado pelo backend em `GET /runs/ranking`; o aplicativo pode usar `SavitApi.GetRanking`.

Exemplo de payload:

```json
{
  "runData": {
    "user_id": "uuid-do-usuario",
    "started_at": "2026-11-01T18:19:44.138Z",
    "finished_at": "2026-11-01T18:20:24.700Z",
    "status": "finished",
    "total_points": 460,
    "total_errors": 4,
    "app_version": "0.5.0"
  },
  "scoresData": [
    { "points": 80, "stage": "ram" },
    { "points": 100, "stage": "motherboard" }
  ]
}
```

## Fluxo legado do Arduino

Os passos abaixo são mantidos apenas para compatibilidade com instalações antigas que ainda possuem `ArduinoLoginController`. O fluxo principal atual é o login por e-mail descrito acima.

## 📋 Passo a Passo

### 1. Abrir a Cena MainMenu
- No Unity, vá em `Assets/Scenes/`
- Abra `MainMenu.unity`

### 2. Criar GameObject para o ArduinoLoginController

**Opção A - Adicionar ao objeto existente:**
- Selecione o GameObject que tem o `MenuController` (geralmente Canvas ou um objeto vazio)
- No Inspector, clique em `Add Component`
- Digite `ArduinoLoginController` e adicione

**Opção B - Criar novo GameObject:**
- Clique direito na Hierarchy
- Create Empty → Renomeie para "ArduinoLoginManager"
- Add Component → `ArduinoLoginController`

### 3. Configurar o ArduinoLoginController

No Inspector do objeto com `ArduinoLoginController`:
- **Listen Port**: `5000` (deixe como está)
- **Expected Result**: `abc123` (ou o código que sua API envia)
- **Login Panel**: Deixe vazio por enquanto (opcional)
- **Status Text**: Deixe vazio por enquanto (opcional)

### 4. Conectar ao MenuController

- Selecione o objeto que tem o componente `MenuController`
- No Inspector, procure o campo `Arduino Login Controller`
- Arraste o objeto que contém o `ArduinoLoginController` para este campo

### 5. Verificar o Botão "Jogar"

- Selecione o botão "Jogar" na Hierarchy
- No Inspector, vá até o componente `Button`
- Na seção `OnClick()`, verifique se está chamando:
  - Objeto: O GameObject com `MenuController`
  - Função: `MenuController > StartGame()`

## ✅ Teste Rápido

1. **Salve a cena** (Ctrl+S)
2. **Execute o jogo** (Play)
3. **Abra o Console** (Window > General > Console)
4. **Clique em "Jogar"**

### O que você deve ver no Console:
```
[MenuController] Botão 'Jogar' clicado!
[MenuController] Iniciando sistema de login Arduino...
[ArduinoLogin] Servidor HTTP iniciado na porta 5000
[ArduinoLogin] Aguardando requisição POST em http://localhost:5000/login
[ArduinoLogin] Aguardando autorização do Arduino...
```

## 🧪 Testar sem Arduino

Com o jogo rodando, abra PowerShell e execute:

```powershell
curl -X POST http://localhost:5000/login -H "Content-Type: application/json" -d '{\"result\":\"abc123\"}'
```

Você deve ver:
- Console Unity: `[ArduinoLogin] ✓ Login autorizado!`
- O jogo deve carregar automaticamente

## ❌ Problemas Comuns

### "ArduinoLoginController não está atribuído"
- Você esqueceu o Passo 4
- Arraste o objeto com ArduinoLoginController para o campo no MenuController

### "Servidor HTTP iniciado" não aparece
- O ArduinoLoginController não está no objeto correto
- Verifique se o componente foi adicionado

### Nada acontece ao clicar em Jogar
- Verifique o botão (Passo 5)
- Certifique-se que está chamando MenuController.StartGame()

### "Access to path denied" ou erro de permissão
- Execute o Unity como Administrador
- Ou mude a porta para 8080 ou outro número

## 🎨 UI Opcional (Para Feedback Visual)

Se quiser mostrar uma tela de "Aguardando Login":

1. **Criar Panel:**
   - Hierarchy > Create > UI > Panel
   - Renomeie para "LoginPanel"
   - Posicione no centro da tela

2. **Adicionar Text:**
   - Clique direito em LoginPanel > Create > UI > Text
   - Renomeie para "StatusText"
   - Configure texto: "Aguardando autenticação..."
   - Ajuste tamanho e posição

3. **Conectar ao ArduinoLoginController:**
   - Selecione o objeto com ArduinoLoginController
   - Arraste LoginPanel para o campo "Login Panel"
   - Arraste StatusText para o campo "Status Text"

4. **Desativar o Panel:**
   - Selecione LoginPanel
   - Desmarque o checkbox no topo do Inspector

## 📝 Checklist Final

- [ ] ArduinoLoginController adicionado à cena
- [ ] MenuController tem referência ao ArduinoLoginController
- [ ] Botão Jogar chama MenuController.StartGame()
- [ ] Testado no Unity (Console mostra mensagens)
- [ ] Testado com curl (Login funciona)

Pronto! Agora quando clicar em Jogar, o Unity vai aguardar a requisição da sua API Python! 🚀

## Observação final

O fluxo legado do Arduino permanece documentado acima apenas para instalações antigas. Para o fluxo atual, use as seções de API, menu e pontuação no início deste arquivo.
