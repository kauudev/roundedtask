# RoundedTask

RoundedTask é um aplicativo leve para Windows 11 que deixa a barra de tarefas com cantos arredondados e layouts mais modernos, sem usar overlays, sem modificar arquivos do sistema e sem substituir a barra original do Windows.

O objetivo do projeto é oferecer uma personalização bonita, simples e reversível, mantendo o impacto no desempenho praticamente imperceptível.

## Destaques

- Interface moderna em português.
- Ícone próprio no aplicativo, na bandeja do sistema e no executável.
- Aplicação automática das mudanças enquanto você ajusta as configurações.
- Suporte à barra principal e a barras de monitores secundários.
- Opção para iniciar junto com o Windows.
- Opção para restaurar a barra ao sair.
- Sem Electron, sem serviços extras, sem telemetria e sem processos pesados.
- Sem barras falsas por cima da barra do Windows.

## Recursos

### Layouts

RoundedTask trabalha diretamente com a região visível da barra de tarefas real do Windows.

Os layouts disponíveis são:

- **Barra inteira**: arredonda a barra de tarefas completa.
- **Centro + sistema**: cria duas áreas visíveis, uma em volta dos aplicativos centrais e outra em volta da área do sistema, onde ficam rede, volume, ícones ocultos, data e hora.

### Ajustes visuais

O aplicativo permite configurar:

- Raio dos cantos.
- Margem superior.
- Margem inferior.
- Margem esquerda.
- Margem direita.
- Largura da área central.
- Posição da área central.
- Largura da área do sistema.
- Recuo direito da área do sistema.

### Predefinições

Também existem predefinições prontas para começar rápido:

- Balanceado
- Compacto
- Flutuante
- Pílula completa
- Centro e sistema
- Personalizado

## Instalação

Baixe o executável mais recente na página de releases do projeto e execute:

```text
RoundedTask.exe
```

Ao abrir o aplicativo normalmente, a tela de configurações aparece direto.

Se o RoundedTask já estiver rodando em segundo plano, abrir o executável novamente apenas traz a janela de configurações para frente.

## Como usar

1. Abra o `RoundedTask.exe`.
2. Ative ou desative o arredondamento.
3. Escolha uma predefinição ou ajuste manualmente.
4. Use o layout **Centro + sistema** se quiser separar a área dos aplicativos da área do relógio e dos ícones do sistema.
5. Feche a janela quando terminar. O aplicativo continua rodando na bandeja do sistema.

Para abrir as configurações novamente, clique duas vezes no ícone do RoundedTask na área de ícones ocultos ou abra o executável mais uma vez.

## Inicialização com o Windows

A opção **Iniciar com o Windows** registra o aplicativo em:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

O RoundedTask inicia com o argumento `--tray`, mantendo a janela oculta e deixando apenas o funcionamento em segundo plano.

## Comandos úteis

Restaurar a barra de tarefas manualmente:

```powershell
.\RoundedTask.exe --restore
```

Aplicar a configuração uma única vez:

```powershell
.\RoundedTask.exe --apply-once
```

Iniciar direto na bandeja:

```powershell
.\RoundedTask.exe --tray
```

## Build

O projeto usa C# com Windows Forms e pode ser compilado com o compilador do .NET Framework que já vem no Windows.

Na pasta do projeto, execute:

```powershell
.\build.ps1
```

O executável será gerado em:

```text
bin\RoundedTask.exe
```

Os assets do aplicativo são copiados para:

```text
bin\assets
```

## Estrutura do projeto

```text
RoundedTask
+-- assets
|   +-- roundedtask.ico
|   +-- roundedtask.png
+-- src
|   +-- AppSettings.cs
|   +-- AssemblyInfo.cs
|   +-- NativeMethods.cs
|   +-- Program.cs
|   +-- RoundedTaskContext.cs
|   +-- SettingsForm.cs
|   +-- StartupManager.cs
|   +-- TaskbarStyler.cs
|   +-- app.manifest
+-- build.ps1
+-- README.md
```

## Como funciona

RoundedTask localiza a barra de tarefas do Windows e aplica uma região arredondada usando APIs nativas do Windows, principalmente `SetWindowRgn`.

Isso significa que o aplicativo não desenha uma barra falsa por cima da barra original. Ele apenas ajusta a área visível da própria barra de tarefas.

Quando o Explorer reinicia ou recria a barra, o RoundedTask detecta o evento e reaplica a configuração.

## Configurações

As configurações são salvas em:

```text
%APPDATA%\RoundedTask\settings.ini
```

Esse arquivo guarda os valores da interface, incluindo layout, margens, raio dos cantos, largura das áreas e opções gerais.

## Segurança e desempenho

RoundedTask foi pensado para ser conservador:

- Não injeta código no Explorer.
- Não altera arquivos do Windows.
- Não usa overlay permanente.
- Não usa navegador embutido.
- Não envia dados para nenhum servidor.
- Não mantém interface pesada aberta em segundo plano.

Em uso normal, a janela de configurações fica fechada e apenas o processo leve da bandeja continua ativo para manter a barra aplicada.

## Restauração

Se quiser desfazer a personalização:

1. Abra as configurações.
2. Clique em **Restaurar**.

Ou execute:

```powershell
.\RoundedTask.exe --restore
```

Se a opção **Restaurar ao sair** estiver ativada, a barra também é restaurada quando o aplicativo é encerrado.

## Requisitos

- Windows 11.
- .NET Framework disponível no sistema.
- Permissão normal de usuário. Não é necessário executar como administrador para o uso comum.

## Observação

RoundedTask personaliza um componente real do Windows. Por isso, diferentes versões do Windows 11, escalas de tela, layouts de monitores e atualizações do Explorer podem afetar o comportamento visual.

O projeto evita técnicas mais arriscadas e mantém caminhos simples de restauração para que qualquer ajuste seja reversível.
