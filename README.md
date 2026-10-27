# StreamCell

Use o celular Android como um painel de teclas para controlar ações do seu PC. O deck é configurado no aplicativo do Windows; no celular, você só conecta e usa as páginas que já foram criadas.

## Downloads

Baixe a versão mais recente em [Releases](https://github.com/Claudi01/StreamCell/releases/latest):

- **Windows:** StreamCell.exe ou StreamCell-Windows.zip
- **Android:** StreamCell-Android.apk

No Windows, abra StreamCell.exe. Ele abre o editor em uma janela própria e mantém o servidor ativo enquanto o programa estiver aberto. A primeira inicialização pode pedir permissão do Firewall do Windows para a rede privada.

No Android, instale o APK. Se o Android pedir autorização, permita instalar apps obtidos por essa fonte. Deixe o PC e o celular na mesma rede local.

## Conectar o celular

1. Conecte o PC e o celular à mesma rede Wi-Fi.
2. No editor do PC, selecione **Conectar celular** e leia o QR com o StreamCell Android. O QR preenche o endereço e o código temporário.
3. Confira o endereço e o código preenchidos no app e toque em **Conectar**. Não há usuário ou senha para criar.

Se o QR não abrir o app, informe manualmente no Android o endereço mostrado pelo PC e o código temporário. Celular e computador precisam continuar na mesma rede.

## Configurar o deck

O editor oferece ações de computador e de live:

- Abrir aplicativos e sites, enviar teclas de atalho e controlar mídia/volume.
- Tocar e parar sons no Soundpad.
- Controlar cenas, áudio, transmissão e gravação no OBS.
- Criar várias páginas, escolher de 2 a 8 colunas e de 2 a 6 linhas por página, e trocar o ícone por uma foto PNG, JPG ou WebP.

Para OBS, ative o servidor WebSocket nas configurações do OBS. A porta e a senha ficam salvas localmente; a senha é protegida pelo Windows para a conta atual. O caminho do Soundpad e o deck também ficam na pasta local do StreamCell.

O código de conexão é temporário e o PC limita as tentativas. O token do celular fica guardado no próprio Android até você desconectá-lo. O aplicativo do PC e o Android conversam apenas pela rede local e não usam um servidor na nuvem.

## Atualizar sem perder o workspace

O botão **Atualizar** abre a página oficial de versões. Feche o StreamCell, baixe o novo executável e substitua o antigo. No Android, instale o APK da versão nova por cima do app atual. O Windows conserva o deck e as configurações em %LOCALAPPDATA%\StreamCell; a atualização do executável não substitui essa pasta. A assinatura beta consistente permite atualizar o APK mantendo a conexão salva no celular.

O sistema de compilação publica executáveis separados por versão. O arquivo SHA256SUMS.txt na Release permite conferir os hashes dos downloads.

## Privacidade do repositório

O repositório contém código-fonte e uma chave beta de assinatura do APK, sem o deck, senha do OBS ou outros dados do computador. Leia SECURITY.md para entender o limite dessa chave de demonstração antes de instalar versões.

## Compilar

- Windows: instale o .NET Framework 4.8 e execute build.ps1.
- Android: instale JDK 17 e Gradle 8.9; em android/, execute gradle :app:assembleRelease.