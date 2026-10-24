# PB - LAN Helper

Aplicativo para Windows que **instala e limpa o jogo em poucos cliques**, feito para manter dezenas de máquinas funcionando durante uma LAN ou mini-campeonato. Quando o jogo dá problema em algum PC, o jogador reinstala em minutos e volta a jogar.

![Mockup das telas do PB - LAN Helper](docs/mockup.png)

---

## Sumário

- [Recursos](#recursos)
- [Como funciona](#como-funciona)
- [Requisitos](#requisitos)
- [Uso rápido](#uso-rápido)
- [Configurações](#configurações)
- [Instalar](#instalar)
- [Limpar](#limpar)
- [Compilar e gerar o .exe](#compilar-e-gerar-o-exe)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Dicas e solução de problemas](#dicas-e-solução-de-problemas)
- [Histórico de versões](#histórico-de-versões)

---

## Recursos

- Interface escura, simples e rápida de operar (janela 800x600), com telemetria passo a passo e barra de progresso.
- Dois botões na tela inicial: **Instalar** e **Limpar**.
- **Origens de rede (SMB) em lista**: adicione quantas quiser, na ordem de prioridade, e ative ou desative cada uma.
- **Arquivos `.zip` em lista**: adicione quantos quiser e ative ou desative cada um.
- **Failover automático durante a cópia**: se o SMB principal cair no meio do arquivo, o app troca para o próximo e continua do byte em que parou, sem recomeçar.
- **Verificação estilo rsync**: antes de copiar, compara o que já existe localmente e só copia o que falta, está incompleto ou está desatualizado.
- **Cópia segura**: o arquivo é gravado como `.part` e só vira `.zip` quando termina por completo. Nunca fica um zip pela metade.
- **Extração paralela**: usa vários núcleos do processador e pula arquivos que já estão idênticos.
- **Atalhos automáticos** na área de trabalho do usuário atual (um por zip, por exemplo `LIVE` e `QA`).
- **Limpeza completa**: apaga a pasta do jogo e a pasta `Zepetto` do AppData.
- Configurações salvas no **Registro do Windows** (sem arquivo JSON solto ao lado do `.exe`).
- Arquivo único (`.exe`) que já leva o .NET dentro. Não precisa instalar nada nas máquinas.

## Como funciona

```
Instalar
 ├─ Verifica quais origens SMB respondem (em paralelo, com timeout)
 ├─ Encerra o jogo, se estiver aberto
 ├─ Cria a pasta de destino (padrão: C:\Zepetto)
 ├─ Compara os zips locais com os da rede (tamanho, data e integridade)
 ├─ Copia o que for necessário, com troca automática de origem se uma cair
 ├─ Extrai cada zip (em paralelo, pulando arquivos idênticos)
 └─ Cria os atalhos na área de trabalho

Limpar
 ├─ Encerra o jogo, se estiver aberto
 ├─ Apaga a pasta de destino e tudo que há dentro dela
 ├─ Apaga a pasta Zepetto do AppData (Roaming e Local)
 └─ Confere se sobrou algo
```

## Requisitos

**Para usar o app nas máquinas:**

- Windows 10 ou Windows 11 (64 bits)
- Acesso de rede às pastas SMB de origem (unidades mapeadas como `Z:` e `Y:` ou caminhos UNC)

**Para compilar:**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git (para clonar)

## Uso rápido

1. Copie o `PBLanHelper.exe` para a máquina.
2. Dê dois cliques para abrir. **Não execute como administrador** (veja [Dicas](#dicas-e-solução-de-problemas)).
3. Na primeira vez, abra **Configurações** e confira as origens, os zips e a pasta de destino.
4. Clique em **Instalar** para instalar ou reinstalar, ou em **Limpar** para remover tudo.

## Configurações

Abra pelo botão **Configurações** no canto inferior direito da tela inicial.

| Campo | Para que serve | Exemplo |
|---|---|---|
| **Origens de rede (SMB)** | Pastas de rede onde ficam os zips, em ordem de prioridade. A primeira ativa que responder é usada, e as seguintes são reserva. | `z:\master`, `y:\backup` |
| **Arquivos .zip** | Caminho de cada zip **dentro** de cada origem. Só aceita arquivos `.zip`. | `LIVE.zip`, `QA.zip` |
| **Pasta de destino** | Onde o jogo será instalado. | `C:\Zepetto` |

Cada linha das listas tem uma caixa de seleção para **ativar ou desativar**, um botão para **escolher a pasta ou o arquivo** pela janela do Windows e uma lixeira para **remover**. Clique em **Salvar configurações** ao terminar.

Dica: as origens de reserva devem ter os **mesmos arquivos** que a principal. Se o tamanho do arquivo for diferente, o app ignora essa origem para aquele arquivo.

**Onde ficam salvas:** no Registro, em `HKEY_CURRENT_USER\Software\PBLanHelper`, nas chaves `Smbs`, `Zips` e `Destination`.

## Instalar

1. **Rede:** testa todas as origens ativas ao mesmo tempo (limite de 6 segundos cada).
2. **Jogo aberto:** encerra `PointBlank.exe` e `PBLauncher.exe`, se estiverem rodando.
3. **Comparação (estilo rsync):** um zip local é considerado completo quando:
   - existe e abre como zip válido;
   - tem o mesmo tamanho do zip da rede;
   - não é mais antigo que o da rede.

   Se estiver completo, a cópia é **pulada**. Se estiver incompleto, corrompido ou desatualizado, é copiado de novo. Se a rede estiver indisponível e existir uma cópia local completa, o app usa a cópia local.
4. **Cópia com failover:** copia para `arquivo.zip.part` e renomeia ao terminar. Se uma origem ficar mais de 6 segundos sem responder ou der erro, troca para a próxima origem e continua do mesmo ponto. A tela mostra tamanho copiado, velocidade e a origem em uso.
5. **Extração paralela:** extrai cada zip em `C:\Zepetto\<nome do zip>`. Se o zip já vier com a pasta raiz (por exemplo `LIVE/...`), extrai direto em `C:\Zepetto`. Arquivos que já existem com o mesmo tamanho e a mesma data são pulados.
6. **Atalhos:** procura o `PointBlank\PBLauncher.exe` dentro de cada pasta extraída e cria um atalho na área de trabalho com o nome do zip (`LIVE`, `QA`).

Estrutura esperada depois da instalação:

```
C:\Zepetto\
 ├─ LIVE.zip
 ├─ QA.zip
 ├─ LIVE\PointBlank\PBLauncher.exe
 └─ QA\PointBlank\PBLauncher.exe
```

> Os zips locais ficam em `C:\Zepetto` de propósito. Assim, a próxima instalação é bem mais rápida, porque não precisa baixar tudo de novo.

**Reinstalar do zero:** para garantir um jogo sem arquivos corrompidos, use **Limpar** e depois **Instalar**. Um **Instalar** direto repõe arquivos alterados ou faltando, mas é pensado para ser rápido.

## Limpar

Mostra uma tela de confirmação e, ao aceitar:

1. Encerra o jogo, se estiver aberto.
2. Apaga a pasta de destino (padrão `C:\Zepetto`) e tudo que há dentro dela.
3. Apaga a pasta `Zepetto` em `%APPDATA%` e em `%LOCALAPPDATA%`.
4. Confere se sobrou algo e avisa em caso de falha.

Por segurança, o app **recusa limpar** se a pasta de destino for a raiz de um disco, a pasta do usuário ou a pasta do Windows.

## Compilar e gerar o .exe

```powershell
git clone https://github.com/scanery200/pb-lan-helper.git
cd pb-lan-helper
dotnet run
```

Para gerar o executável único (inclui o .NET, não precisa instalar nada nas máquinas):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist
```

O resultado fica em `dist\PBLanHelper.exe`.

## Estrutura do projeto

| Arquivo | Função |
|---|---|
| `PBLanHelper.csproj` | Configuração do projeto (.NET 10, WPF, versão) |
| `GlobalUsings.cs` | Importações globais (`System.IO`) |
| `App.xaml` | Tema escuro e estilos (botões, caixas de texto, barras de progresso) |
| `MainWindow.xaml` / `.cs` | Tela inicial e tela de Configurações |
| `ProgressWindow.xaml` / `.cs` | Tela de progresso, confirmação da limpeza e resultado |
| `TitleBar.xaml` / `.cs` | Barra de título personalizada |
| `Step.cs` | Lista de passos e cálculo do progresso |
| `Operations.cs` | Toda a lógica de Instalar e Limpar (rede, cópia, extração, atalhos) |
| `AppSettings.cs` | Leitura e gravação das configurações no Registro |

## Dicas e solução de problemas

- **Não rode como administrador.** O Windows não enxerga unidades de rede mapeadas (`Z:`, `Y:`) em processos elevados. Executando normalmente, o app cria a pasta no `C:` sem problemas.
- **Extração lenta?** O antivírus do Windows (Defender) analisa cada arquivo extraído. Em cada máquina, uma vez, rode no PowerShell **como administrador**:
  ```powershell
  Add-MpPreference -ExclusionPath "C:\Zepetto"
  ```
- **"Nenhuma origem de rede acessível":** confira se o mapeamento está conectado (abra `Z:` no Explorer) e se os caminhos nas Configurações estão corretos.
- **"Arquivo não encontrado em nenhuma origem":** o nome do zip nas Configurações precisa ser igual ao arquivo na rede (exemplo `LIVE.zip`).
- **A cópia trocou de origem sozinha:** é o failover funcionando. A tela mostra "Origem ... indisponível, trocando…".
- **Falha ao apagar:** feche o jogo e o launcher e clique em **Tentar novamente**.
- **Janela do VS Code mostrando erro de NuGet (`wpftmp`):** é um aviso do C# Dev Kit e não afeta o build. Abra o VS Code direto na pasta do projeto ou confirme com `dotnet build`.
- **Globalização:** o projeto usa `InvariantGlobalization` desativado (`false`). Ativar essa opção faz o app fechar sozinho em Windows configurado em pt-BR.

## Histórico de versões

### 1.0.1
- Origens SMB e arquivos zip em listas configuráveis, com ativar e desativar.
- Failover imediato entre origens durante a cópia, com retomada do ponto em que parou.
- Verificação estilo rsync antes de copiar (existe, completo, íntegro e atualizado).
- Cópia segura com arquivo `.part`.
- Extração paralela, pulando arquivos idênticos.
- Correção do fechamento inesperado em Windows pt-BR.

### 1.0.0
- Primeira versão com **Instalar** e **Limpar**, tela de Configurações e telemetria com barra de progresso.