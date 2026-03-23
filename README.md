# command-to-translate

Aplicativo Windows residente na bandeja do sistema para traduzir texto sob demanda de `pt-BR` para `en-US` usando Ollama. O foco do projeto é acelerar a escrita em inglês dentro de campos de texto, terminais e TUIs sem exigir troca manual de janela ou copiar e colar o texto manualmente.

## Visao geral

Quando o atalho global e acionado, a aplicacao:

1. Descobre qual janela esta em foco.
2. Tenta capturar o texto selecionado atual.
3. Se nao houver selecao, tenta capturar o trecho anterior ao cursor de acordo com o host.
4. Envia o texto para o modelo configurado no Ollama.
5. Substitui o texto original pela traducao.
6. Restaura o conteudo original da area de transferencia.

O comportamento padrao e otimizado para traduzir o que voce acabou de digitar, especialmente em terminais e caixas de texto comuns.

## Principais funcionalidades

- Atalho global configuravel, com padrao `Ctrl + Shift + T`.
- Execucao em segundo plano com icone na system tray.
- Traducao deterministica de `pt-BR` para `en-US` via Ollama.
- Suporte a diferentes tipos de host:
  - campos de texto genericos;
  - Windows Terminal;
  - console classico do Windows (`conhost`);
  - terminais Electron/xterm.js com buffer de teclas.
- Restauracao automatica da area de transferencia apos a traducao.
- Protecao contra traducao acidental em campos de senha.
- Health check periodico do Ollama a cada 30 segundos.
- Suite de testes unitarios cobrindo fluxo de captura, adaptadores e interoperabilidade Win32.

## Como o fluxo funciona

### Campos de texto genericos

Se houver texto selecionado, a selecao atual e usada. Caso contrario, o app tenta selecionar do cursor ate o inicio do campo com `Ctrl + Shift + Home`, copia o trecho e substitui pelo texto traduzido.

### Windows Terminal e console classico

O app tenta trabalhar sobre a linha atual. Quando nao encontra uma selecao pronta, usa selecao retroativa a partir do cursor e depois remove o texto original com `Backspace` antes de colar a traducao.

### Terminais Electron / TUI

Em hosts onde nao e possivel criar uma selecao copiavel por atalho de teclado, o app usa um buffer interno de teclas digitadas. Esse buffer acompanha o texto que voce acabou de escrever e, quando o atalho e pressionado, ele:

- consome a frase atual digitada;
- traduz esse conteudo;
- apaga a entrada original com `Backspace`;
- cola a traducao com `Shift + Insert`.

Esse modo foi pensado para cenarios como TUIs rodando em janelas baseadas em Electron.

## Requisitos

- Windows 10 ou Windows 11.
- .NET SDK 9.0 ou superior para desenvolvimento.
- Ollama instalado e acessivel localmente.
- Um modelo compativel com traducao configurado no Ollama.

Configuracao padrao do projeto:

- Endpoint do Ollama: `http://127.0.0.1:11434`
- Modelo: `translategemma`
- Timeout efetivo minimo para chamadas de traducao: `8000 ms`

## Instalacao e execucao

### 1. Clonar o repositorio

```powershell
git clone <URL_DO_REPOSITORIO>
cd command-to-translate
```

### 2. Preparar o Ollama

Se o Ollama ainda nao estiver em execucao:

```powershell
ollama serve
```

Baixe o modelo padrao usado pelo projeto:

```powershell
ollama pull translategemma
```

Se quiser usar outro modelo, altere a chave `Model` no `config.toml`.

### 3. Executar a aplicacao

```powershell
dotnet run --project src\CommandToTranslate.csproj
```

Ao iniciar:

- um icone sera exibido na system tray;
- o arquivo `config.toml` sera criado automaticamente no diretorio base da aplicacao;
- o app registrara o atalho global configurado;
- sera feito um health check inicial no Ollama.

### 4. Publicar um build

```powershell
dotnet publish src\CommandToTranslate.csproj -c Release -r win-x64 --self-contained false
```

O binario publicado sera gerado em um caminho semelhante a:

```text
src\bin\Release\net9.0-windows\win-x64\publish\
```

Nesse modo, o `config.toml` fica ao lado do executavel publicado.

## Configuracao

O arquivo `config.toml` e gerado automaticamente no primeiro start. Durante `dotnet run`, ele fica no diretorio de saida da aplicacao, por exemplo:

```text
src\bin\Debug\net9.0-windows\
```

Tambem e possivel abrir esse arquivo pelo menu do icone na bandeja em `Open config file`.

Exemplo completo:

```toml
[Ollama]
Endpoint = "http://127.0.0.1:11434"
Model = "translategemma"
TimeoutMs = 10000
Temperature = 0.0
Stream = false
KeepAlive = "5m"

[Behavior]
ShortcutStepDelayMs = 35
ClipboardTimeoutMs = 800
HostSettleDelayMs = 60

[Hotkey]
Modifiers = ["Ctrl", "Shift"]
Key = "T"

[Ui]
ShowNotifications = true
NotifyOnError = true
```

### Chaves de configuracao

#### `[Ollama]`

- `Endpoint`: URL base da API do Ollama.
- `Model`: nome do modelo usado para traducao.
- `TimeoutMs`: timeout configurado para o `HttpClient`. O codigo aplica um minimo recomendado de `8000 ms`.
- `Temperature`: temperatura enviada para o modelo.
- `Stream`: habilita ou desabilita streaming na chamada `/api/chat`.
- `KeepAlive`: valor repassado para o Ollama para manter o modelo carregado.

#### `[Behavior]`

- `ShortcutStepDelayMs`: atraso entre eventos sinteticos de teclado.
- `ClipboardTimeoutMs`: tempo maximo de espera pela copia para a area de transferencia.
- `HostSettleDelayMs`: atraso extra para o host estabilizar apos selecao/copias.

#### `[Hotkey]`

- `Modifiers`: lista de modificadores aceitos. O projeto reconhece `Ctrl`, `Control`, `Shift`, `Alt`, `Win` e `Windows`.
- `Key`: tecla principal do atalho global. Suporta letras, numeros, teclas nomeadas e `F1` a `F24`.

#### `[Ui]`

- `NotifyOnError`: controla notificacoes de erro exibidas pela aplicacao.
- `ShowNotifications`: chave existente no modelo de configuracao, mas ainda nao e usada pelo fluxo atual.

## Uso

1. Inicie o app.
2. Digite texto em portugues ou selecione um trecho em um host suportado.
3. Pressione `Ctrl + Shift + T` ou o atalho configurado.
4. Aguarde a substituicao do texto pela traducao.

Comportamento esperado:

- Se houver selecao ativa, ela tem prioridade.
- Se nao houver selecao, o app tenta capturar o texto imediatamente anterior ao cursor.
- Em terminais Electron/TUI, o app usa o buffer de teclas digitadas na janela atual.
- Se o texto ja estiver em ingles, o prompt instrui o modelo a devolve-lo sem alteracao.

## Menu da bandeja

O icone da bandeja oferece as seguintes acoes:

- `Enable hotkey translation`: habilita ou pausa o atalho global.
- `Open config file`: abre o `config.toml` no editor padrao do sistema.
- `About`: exibe informacoes resumidas do app.
- `Exit`: encerra a aplicacao.

Estados sinalizados pelo tooltip do icone:

- `Hotkey ready`
- `Hotkey disabled`
- `Error - Ollama unavailable`

## Hosts suportados

### `GenericTextFieldAdapter`

Voltado para campos de texto tradicionais que nao sejam consoles/terminais. Usa:

- `Ctrl + C` para copiar;
- `Ctrl + Shift + Home` para selecao retroativa;
- digitacao sintetica para inserir a traducao.

### `WindowsTerminalLineAdapter`

Voltado para Windows Terminal e janelas com classe relacionada ao Cascadia. Usa:

- `Ctrl + Shift + C` para copiar;
- `Shift + Home` para selecao da linha;
- `Ctrl + Shift + V` para colar;
- `Backspace` para remover o texto original.

### `ClassicConsoleLineAdapter`

Voltado para `ConsoleWindowClass`, `conhost` e `openconsole`. Usa:

- `Ctrl + C` para copiar;
- `Shift + Home` para selecao retroativa;
- `Ctrl + V` para colar;
- `Backspace` para remover o texto original.

### `ElectronTerminalAdapter`

Voltado para janelas `Chrome_WidgetWin_1`. Nao depende de copia por selecao e usa:

- buffer de teclas digitadas;
- `Backspace` repetido para apagar o trecho original;
- `Shift + Insert` para colar a traducao.

## Estrutura do projeto

```text
.
|-- src
|   |-- Core
|   |-- Hooks
|   |-- Native
|   |-- Services
|   |-- UI
|   `-- CommandToTranslate.csproj
|-- tests
|   `-- CommandToTranslate.Tests
`-- command-to-translate.slnx
```

Resumo dos modulos:

- `src/Core`: configuracao, estado global e tipos centrais.
- `src/Hooks`: hotkey global e hook de teclado para bufferizacao.
- `src/Native`: interoperabilidade Win32.
- `src/Services`: traducao, area de transferencia, foco, despacho de input e adaptadores de host.
- `src/UI`: icone e menu da bandeja.
- `tests/CommandToTranslate.Tests`: testes unitarios com xUnit.

## Testes

Para executar a suite de testes:

```powershell
dotnet test tests\CommandToTranslate.Tests\CommandToTranslate.Tests.csproj
```

Observacao importante: o arquivo `command-to-translate.slnx` atualmente referencia apenas o projeto em `src`, entao `dotnet test command-to-translate.slnx` nao executa a suite de testes.

Cobertura atual da suite:

- adaptadores de traducao por host;
- coordenacao do fluxo de traducao sob demanda;
- comportamento do buffer de teclas;
- validacao basica de estruturas Win32.

## Logs

Os logs sao gravados em:

```text
%APPDATA%\command-to-translate\logs\
```

Formato esperado do arquivo:

```text
command-to-translate-YYYYMMDD.log
```

Os logs incluem:

- startup e shutdown;
- registro do hotkey;
- health checks do Ollama;
- erros nao tratados;
- tentativas de captura e traducao.

## Tratamento de erros e diagnostico

### Hotkey nao registra

Possivel causa:

- o atalho ja esta em uso por outra aplicacao.

Acao recomendada:

- altere `Hotkey.Modifiers` e/ou `Hotkey.Key` no `config.toml`.

### Ollama indisponivel

Sintomas:

- tooltip do tray em estado de erro;
- notificacoes de falha;
- traducao ignorada com mensagem de indisponibilidade.

Acao recomendada:

- confirme que o Ollama esta rodando;
- valide `Ollama.Endpoint`;
- execute `ollama list` para conferir se o modelo existe.

### Modelo nao encontrado

Acao recomendada:

```powershell
ollama pull translategemma
```

Ou ajuste `Ollama.Model` para um modelo ja instalado.

### Nada foi capturado para traducao

Possiveis causas:

- host nao suportado pelo adaptador atual;
- nao havia selecao nem texto capturavel antes do cursor;
- em TUI/Electron, o buffer de teclas nao tinha conteudo util.

### Campo protegido

O projeto evita traduzir texto em campos detectados como senha por seguranca.

## Limitacoes atuais

- A direcao da traducao e fixa: `pt-BR -> en-US`.
- O projeto depende de Win32 e, portanto, e especifico para Windows.
- Em alguns hosts, a heuristica atua apenas sobre o texto anterior ao cursor, nao necessariamente sobre o documento inteiro.
- Em terminais Electron/TUI, o app depende do buffer de teclas digitadas; texto exibido na tela, mas nao digitado na sessao atual, nao entra nesse buffer.
- `Ui.ShowNotifications` ainda nao influencia o fluxo da aplicacao.

## Stack

- `C#`
- `.NET 9`
- `Windows Forms` para message loop e tray icon
- `Win32 API` para hotkeys, hook de teclado e envio de input
- `Tomlyn` para serializacao/deserializacao de `config.toml`
- `xUnit` para testes
