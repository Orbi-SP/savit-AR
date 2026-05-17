# PROJETO SAVIT

<p align="center">
  <img src="Imgs/logo_projeto.png" alt="Logo do Projeto" />
</p>

O Projeto SAVIT (Sistema de Aprendizado Virtual de Infraestrutura de T.I.) é um jogo que guiará você a aprender sobre conteúdos sobre Computadores, DVR, Firewalls, Unifi's, entre outras coisas voltadas ao universo de redes e infraestrutura. 

## Demonstração

Assista a uma demonstração prática do projeto em funcionamento no vídeo abaixo:

[![Assista no YouTube](https://img.youtube.com/vi/AxcnQyhpZwc/0.jpg)](https://www.youtube.com/watch?v=AxcnQyhpZwc)

## Como Utilizar
Como o Software atualmente está em desenvolvimento e não há uma versão de build que funcione corretamente, você precisará ter acesso aos itens abaixo:

- **Unity Engine (Unity Hub)**: Você precisará ter instalado em sua máquina o Unity Engine para executar o jogo, pois como não há uma build funcional para o jogo de fato, você tem que executá-lo no modo de desenvolvimento.
- **Internet**: o Unity vai baixar dependências via UPM (Unity Package Manager) na primeira abertura.
- **Git**: necessário para o UPM baixar o pacote do MediaPipe (dependência via Git).

## Utilização

Após ter acesso a este código através da clonagem do repositório ou através de nossa landing page, você não precisa criar um projeto novo no Unity.

1) Abra o projeto diretamente no Unity Hub:
- No Unity Hub, clique em **Add** (Adicionar)
- Selecione a pasta raiz deste repositório (a pasta que contém `Assets/`, `Packages/` e `ProjectSettings/`)

![image](https://github.com/user-attachments/assets/834ae3b1-aa33-4cba-9a74-e69b4e9d9eaa)
![image](https://github.com/user-attachments/assets/c5e87138-3cb7-43b1-a4c3-e5ec5ee04bbc)

2) Aguarde o Unity importar e baixar dependências:
- Na primeira abertura, o Unity pode demorar um pouco para importar assets e resolver pacotes.
- Se o Package Manager reportar erro ao baixar pacotes, verifique se o **Git** está instalado e disponível no terminal.

3) No Unity, inicie o modo de jogo para testes (Play) e acompanhe as atualizações do projeto.

![image](https://github.com/user-attachments/assets/9903413a-562c-4a03-bc37-63f62976db9a)

Para mais atualizações, estaremos lançando mais updates em nossa landing page, acompanhe em: http://savit-landingpage.s3-website-us-east-1.amazonaws.com/

MediaPipe utilizado (UPM/Git, tag v0.16.3): https://github.com/homuler/MediaPipeUnityPlugin.git?path=Packages/com.github.homuler.mediapipe#v0.16.3
Referência do pacote no repositório: https://github.com/homuler/MediaPipeUnityPlugin/tree/v0.16.3/Packages/com.github.homuler.mediapipe

Obs.: o Unity Package Manager não aceita URL direta para arquivos `.tgz`. Por isso o pacote é baixado via Git, e os binários necessários para Android/Protobuf ficam versionados em `Assets/Plugins/`.