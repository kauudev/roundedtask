# RoundedTask

RoundedTask é um aplicativo leve para Windows 10 e Windows 11 que deixa a barra de tarefas real do Windows com cantos arredondados e layouts mais modernos, sem usar overlays, sem modificar arquivos do sistema e sem substituir a barra original.

O objetivo do projeto é oferecer uma personalização bonita, simples e reversível, mantendo o impacto no desempenho praticamente imperceptível.

## Destaques

- Interface moderna em português.
- Ícone próprio no aplicativo, na bandeja do sistema e no executável.
- Aplicação automática das mudanças enquanto você ajusta as configurações.
- Suporte à barra principal e a barras de monitores secundários.
- Suporte melhorado para Windows 10 e Windows 11.
- Layout de barra inteira.
- Layout Centro + sistema, com área central e área do sistema separadas.
- Layout Segmentos automáticos, que mede os botões reais da barra para separar aplicativos e sistema.
- Ajustes por DPI em cada monitor.
- Compatibilidade opcional com TranslucentTB.
- Opção para transformar a barra em inteira quando há janela maximizada ou uso do Alt+Tab.
- Opção para mostrar a área do sistema apenas ao passar o mouse.
- Campos numéricos editáveis diretamente, com aplicação ao vivo.
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
- **Segmentos automáticos**: detecta os botões reais da barra de tarefas e cria segmentos separados para aplicativos e sistema. No Windows 11, o app usa UI Automation para medir os botões atuais da barra quando as APIs clássicas não entregam a largura correta.

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
- Escala por DPI em cada monitor.

No modo **Segmentos automáticos**, as margens esquerda e direita ficam bloqueadas porque a largura horizontal é calculada automaticamente a partir dos aplicativos e da área do sistema.

### Comportamento inteligente

RoundedTask também inclui opções para:

- Mostrar a área do sistema separada dos aplicativos.
- Mostrar a área do sistema apenas ao passar o mouse.
- Usar barra inteira quando uma janela está maximizada.
- Usar barra inteira durante Alt+Tab ou Task Switcher.
- Reaplicar automaticamente quando o Explorer recria a barra, quando há mudanças de monitor, tema, escala ou configuração do sistema.
- Melhorar a compatibilidade visual com TranslucentTB.

### Predefinições

Existem predefinições prontas para começar rápido:

- Balanceado
- Compacto
- Flutuante
- Pílula completa
- Centro e sistema
- Segmentos automáticos
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
4. Use **Centro + sistema** se quiser separar manualmente a área dos aplicativos da área do relógio e dos ícones do sistema.
5. Use **Segmentos automáticos** se quiser que o RoundedTask detecte sozinho o tamanho dos aplicativos e da área do sistema.
6. Feche a janela quando terminar. O aplicativo continua rodando na bandeja do sistema.

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
|   +-- TaskbarDiscovery.cs
|   +-- TaskbarStyler.cs
|   +-- app.manifest
+-- build.ps1
+-- README.md
+-- RELEASE_v1.1.0.md
```

## Como funciona

RoundedTask localiza a barra de tarefas do Windows e aplica uma região arredondada usando APIs nativas do Windows, principalmente `SetWindowRgn`.

Isso significa que o aplicativo não desenha uma barra falsa por cima da barra original. Ele apenas ajusta a área visível da própria barra de tarefas.

Para o modo **Segmentos automáticos**, o app combina detecção por janelas nativas do Explorer com UI Automation, especialmente no Windows 11, onde alguns botões da barra não aparecem corretamente nas APIs clássicas.

Quando o Explorer reinicia ou recria a barra, o RoundedTask detecta o evento e reaplica a configuração.

## Configurações

As configurações são salvas em:

```text
%APPDATA%\RoundedTask\settings.ini
```

Esse arquivo guarda os valores da interface, incluindo layout, margens, raio dos cantos, largura das áreas, escala por DPI e opções de comportamento.

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

- Windows 10 ou Windows 11.
- .NET Framework disponível no sistema.
- Permissão normal de usuário. Não é necessário executar como administrador para o uso comum.

## Observação

RoundedTask personaliza um componente real do Windows. Por isso, diferentes versões do Windows 10 e Windows 11, escalas de tela, layouts de monitores e atualizações do Explorer podem afetar o comportamento visual.

Os cantos arredondados são feitos por região nativa do Windows. Essa técnica é leve e reversível, mas não oferece antialias perfeito; em alguns raios e escalas, os cantos podem parecer um pouco pixelados.

O projeto evita técnicas mais arriscadas e mantém caminhos simples de restauração para que qualquer ajuste seja reversível.
