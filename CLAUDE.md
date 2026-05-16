# AutoUpdater — Visão Geral da Solução

## O que é esta solução

AutoUpdater é uma solução .NET 10 multiplataforma (Windows, Linux, macOS) para atualização automática de aplicativos. Ela fornece toda a infraestrutura necessária para que um programa possa verificar se existe uma versão mais nova disponível online, fazer o download da nova versão e instalar os arquivos — tudo de forma integrada e transparente para o usuário final.

Deve ser uma solução simples pequena que possa ser compilada em Native AOT quando possível.

O fluxo geral é: o programa cliente usa **AutoUpdaterHelp** para verificar se há atualização disponível; se houver, **AutoUpdaterHelp** inicia o executável do **AutoUpdaterConsole** (ou GUI), que por sua vez faz o download do `.zip` com a nova versão e substitui os arquivos em disco, usando a lógica central do **AutoUpdaterModel**.

---

## Projetos da Solução

### AutoUpdaterModel (`src/AutoUpdateModel`)
**Tipo:** Class Library (.NET 10)

Contém a lógica principal e reutilizável da solução. É referenciado pelos demais projetos.

Principais responsabilidades:
- `Services.ProcessArg` — processa e valida os argumentos de linha de comando recebidos pelo executável atualizador (versão antiga, versão nova, URL de download, pasta de instalação, e-mail para reporte de erros, nome do programa).
- `Services.ReplaceFiles` — executa a substituição dos arquivos da aplicação: apaga os arquivos da versão antiga na pasta de destino, descompacta o `.zip` com a nova versão e, em sistemas não-Windows, concede permissão de execução ao binário (via `chmod 700`).
- `Services.CheckOS` — detecta o sistema operacional em execução (Windows = 0, Linux = 1, macOS = 2) para que o comportamento seja ajustado conforme a plataforma.

Dependências externas (BaseLibrary): `BaseLibrary.Console`, `BaseLibrary.File`.

---

### AutoUpdaterConsole (`src/AutoUpdaterConsole`)
**Tipo:** Aplicação Console (.NET 10, executável)

É a implementação CLI (Command-Line Interface) do atualizador — o processo que efetivamente executa a atualização. É iniciado como um processo filho pelo programa que está sendo atualizado (via **AutoUpdaterHelp**).

Principais responsabilidades:
- Recebe 6 argumentos de linha de comando: versão antiga, versão nova, URL de download, pasta de instalação, e-mail para reporte e nome do programa.
- Verifica conectividade com a internet antes de tentar o download.
- Faz o download do arquivo `.zip` da nova versão a partir da URL fornecida, exibindo barra de progresso no terminal.
- Chama `Services.ReplaceFiles` para substituir os arquivos instalados.
- Em caso de erro, exibe a mensagem em vermelho no console e opcionalmente envia um e-mail de reporte via `ExceptionMethods.SendException`.

Dependências: `AutoUpdaterModel`, `BaseLibrary.Console`, `BaseLibrary.HTTP`, `BaseLibrary.Exception`.

---

### AutoUpdaterHelp (`src/AutoUpdateHelp`)
**Tipo:** Class Library (.NET 10)

É a biblioteca que deve ser referenciada e usada diretamente pelo programa que precisa ser atualizado. Atua como a "cola" entre o programa cliente e o mecanismo de atualização.

Principais responsabilidades:
- `AutoUpdater.HasNewVersion(urlVersion, frequency)` — verifica (respeitando uma frequência mínima de verificação configurável) se existe uma versão mais nova disponível online. Baixa um arquivo de texto remoto com a versão atual e a URL de download por plataforma, compara com a versão do assembly em execução e retorna a nova versão e URL caso exista atualização.
- `AutoUpdater.VerifyUpdateOfAutoUpdater(downloadNotifier)` — verifica e instala/atualiza o próprio executável do AutoUpdater (AutoUpdaterConsole ou AutoUpdaterGUI) dentro da subpasta `AutoUpdater/` no diretório do programa. Garante que o atualizador em si esteja sempre na versão mais recente antes de usá-lo.
- `AutoUpdater.Update(verOnline, urlToUpdate, emailToReportIssue, downloadNotifier)` — inicia o processo de atualização: localiza o executável do AutoUpdater (Console ou GUI, dependendo do OS) e o lança como um novo processo passando todos os argumentos necessários.

O arquivo de versão remoto segue o formato:
```
<numero_da_versao>
<url_windows>
<url_linux>
<url_macos>
```

Dependências: `AutoUpdaterModel`, `BaseLibrary.HTTP`.


## Arquitetura e Dependências

```
Programa do usuário
    └── referencia AutoUpdaterHelp
            └── referencia AutoUpdaterModel
                    └── BaseLibrary.*

AutoUpdaterHelp (em runtime)
    └── inicia processo: AutoUpdaterConsole.exe (ou GUI)
            └── referencia AutoUpdaterModel
```

A solução depende de uma biblioteca interna chamada **BaseLibrary**, localizada em `../BaseLibrary/src/`, que fornece utilitários para: console (`BaseLibrary.Console`), arquivos (`BaseLibrary.File`), HTTP/download (`BaseLibrary.HTTP`), exceções (`BaseLibrary.Exception`) e outros.
