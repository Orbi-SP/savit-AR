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
