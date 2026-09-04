# AutoUpdater: atualizacao automatica de aplicativos

Solucao .NET 10 multiplataforma (Windows, Linux, macOS) para atualizacao automatica:
verificar versao nova online, baixar e instalar os arquivos, de forma integrada e
transparente para o usuario final. Deve continuar simples e pequena, compilavel em
Native AOT quando possivel. Regras compartilhadas em `../CLAUDE.md`.

Cinco projetos daqui entram no `Sindarin.sln`: mudanca de API afeta o Sindarin.

## Fluxo

O programa cliente usa **AutoUpdaterHelp** para verificar se ha atualizacao; se houver,
**AutoUpdaterHelp** inicia o executavel do **AutoUpdaterConsole** (ou GUI), que baixa o
`.zip` da nova versao e substitui os arquivos em disco usando o **AutoUpdaterModel**.

```
Programa do usuario
    -> referencia AutoUpdaterHelp
        -> referencia AutoUpdaterModel
            -> BaseLibrary.*
AutoUpdaterHelp (em runtime)
    -> inicia processo: AutoUpdaterConsole.exe (ou GUI)
        -> referencia AutoUpdaterModel
```

## Projetos

### AutoUpdaterModel (`src/AutoUpdateModel`), Class Library
Logica central, referenciada pelos demais.
- `Services.ProcessArg`: processa e valida os argumentos de linha de comando
  (versao antiga, versao nova, URL de download, pasta de instalacao, e-mail para
  reporte de erros, nome do programa).
- `Services.ReplaceFiles`: apaga a versao antiga na pasta de destino, descompacta o
  `.zip` e, fora do Windows, da permissao de execucao ao binario (`chmod 700`).
- `Services.CheckOS`: Windows = 0, Linux = 1, macOS = 2.
- Dependencias: `BaseLibrary.Console`, `BaseLibrary.File`.

### AutoUpdaterConsole (`src/AutoUpdaterConsole`), executavel
CLI que executa a atualizacao; iniciado como processo filho pelo programa atualizado.
- Recebe os 6 argumentos acima; verifica conectividade; baixa o `.zip` com barra de
  progresso; chama `ReplaceFiles`; em erro, mensagem em vermelho e opcionalmente
  e-mail via `ExceptionMethods.SendException`.
- Dependencias: `AutoUpdaterModel`, `BaseLibrary.Console`, `BaseLibrary.HTTP`,
  `BaseLibrary.Exception`.

### AutoUpdaterHelp (`src/AutoUpdateHelp`), Class Library
Biblioteca referenciada pelo programa que precisa ser atualizado.
- `AutoUpdater.HasNewVersion(urlVersion, frequency)`: respeita frequencia minima de
  verificacao; baixa o arquivo remoto de versao, compara com a versao do assembly e
  retorna versao e URL se houver atualizacao.
- `AutoUpdater.VerifyUpdateOfAutoUpdater(downloadNotifier)`: atualiza o proprio
  AutoUpdater na subpasta `AutoUpdater/` do programa antes de usa-lo.
- `AutoUpdater.Update(verOnline, urlToUpdate, emailToReportIssue, downloadNotifier)`:
  localiza o executavel (Console ou GUI conforme o OS) e o lanca com os argumentos.
- Dependencias: `AutoUpdaterModel`, `BaseLibrary.HTTP`.

Arquivo de versao remoto:
```
<numero_da_versao>
<url_windows>
<url_linux>
<url_macos>
```

## Comandos

- `dotnet build AutoUpdater.sln --no-restore`
- `dotnet test AutoUpdater.sln --no-restore`
- Compatibilidade AOT: skill `dotnet-aot-compat` do plugin `dotnet-upgrade`.
