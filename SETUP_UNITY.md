# 🎮 Configuração Rápida - Unity MainMenu

Siga estes passos para configurar o sistema de login Arduino na cena MainMenu:

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

## Configurar a API do SAVIT (Unity 6000.2.6f2)

O projeto já contém o asset `Assets/Resources/SavitApiConfig.asset`. No Unity 6000.2.6f2:

1. Abra `Assets/Resources/SavitApiConfig.asset` no Inspector.
2. Em **Base Url**, mantenha `https://ihrws12500.execute-api.us-east-1.amazonaws.com/default`.
3. O botão **Criar conta** usa diretamente `POST /user`; não é necessário configurar ou abrir um site externo.
4. Não crie outro asset com o mesmo nome em `Resources`; os scripts carregam este asset automaticamente.
5. Salve a cena e execute o menu. O menu fica em retrato; o botão **Jogar** força paisagem antes de carregar a cena `BRP Sample SceneGabinete`.
6. O login usa `POST /auth`; o cadastro usa `POST /user` e entra automaticamente após a criação.
7. Ao terminar ou abandonar a cena `BRP Sample SceneGabinete`, o app envia uma requisição autenticada `POST /runs` com `runData` (incluindo `user_id`) e `scoresData`. Cada item de `scoresData` contém somente `points` e `stage`; ele representa um componente com 100 pontos, menos 10 por erro.
8. O ranking é lido autenticado por `GET /runs/ranking`; ele deve ser calculado pelo backend com as runs e pontuações salvas.

O app envia o token retornado pelo login no cabeçalho `Authorization: Bearer <token>`. Não coloque chaves privadas ou credenciais do banco no projeto Unity.
