# StreamCell

Um painel de teclas no celular para controlar seu PC durante as lives. Você monta o deck no computador; o celular apenas executa os botões configurados.

## Baixar e usar

1. Baixe **StreamCell.exe** em [Releases](https://github.com/Claudi01/StreamCell/releases/latest) no **PC Windows**.
2. Abra o executável. O editor abre no navegador do próprio computador; mantenha a janela do programa aberta.
3. Selecione uma tecla, escolha a ação, o nome, o ícone e a cor. Clique em **Salvar alterações**.
4. Clique em **Conectar celular**. Com ambos na mesma rede, abra no celular o endereço mostrado e digite o código.
5. No navegador do celular, use **Adicionar à tela inicial** para criar um atalho. O painel funciona em Android e iPhone pelo navegador; não há APK nesta versão.

O PC pode estar por cabo e o celular por Wi-Fi, desde que estejam na mesma rede. Se o Windows solicitar acesso à rede, permita somente em sua rede privada. Redes de convidados ou isolamento de dispositivos podem impedir a conexão. Se forem mostrados vários endereços, escolha o do adaptador usado na sua rede doméstica.

## O que você pode configurar no PC

- Até 20 páginas, com 15 teclas por página. Arraste para reorganizar ou escolha a posição.
- Aplicativos e atalhos do Windows (.exe, .lnk, .url), incluindo argumentos opcionais.
- Sites abertos no navegador do computador.
- Sons do Soundpad pelo número na lista e um botão para parar a reprodução.
- Atalhos como CTRL+ALT+1, CTRL+SHIFT+M e F13.
- Reproduzir/pausar, próxima/anterior, volume e mudo do Windows.
- Cenas, mudo de fontes, transmissão e gravação do OBS.
- Confirmação antes de executar ações. Iniciar/parar transmissão sempre exige confirmação.

O celular recebe alterações salvas em aproximadamente dois segundos. Pode navegar entre páginas, executar teclas e se desconectar. Não possui editor, catálogo de aplicativos nem acesso à senha do OBS.

## OBS e Soundpad

No OBS, abra **Ferramentas → Configurações do servidor WebSocket**, ative o servidor e mantenha a autenticação. No editor StreamCell, abra **Configurações**, informe porta e senha, aplique e salve. O OBS deve rodar neste mesmo computador e ser compatível com obs-websocket 5 (incluído no OBS 28 ou superior). A senha não vai para o telefone.

Para Soundpad, mantenha o programa instalado e aberto. Se necessário, escolha Soundpad.exe nas configurações. Em cada tecla de som, informe o índice exibido na lista do Soundpad. Se você reordenar a lista, atualize os índices dos botões. O áudio segue as configurações de saída/microfone do próprio Soundpad; o StreamCell não instala um driver de áudio nem substitui a licença do Soundpad.

Atalhos são enviados à janela em foco no Windows; aplicativos com atalhos globais podem recebê-los em segundo plano. Programas executados como administrador podem não aceitar teclas de um StreamCell sem elevação. Controles de mídia seguem o aplicativo de mídia ativo do Windows.

## Configurações privadas e atualizações

As configurações ficam em **%LOCALAPPDATA%\StreamCell\config.json**, fora da pasta do executável. A senha OBS é protegida pelo Windows (DPAPI, usuário atual). O arquivo pode conter caminhos de aplicativos e parâmetros pessoais: não publique config.json nem sua cópia .bak. Nenhuma configuração pessoal acompanha o código ou o executável distribuído.

Para atualizar, feche o StreamCell, baixe o novo executável nas Releases e substitua o antigo. O painel do celular é servido pelo PC e atualiza junto; recarregue a página depois. Suas configurações permanecem na pasta local. Não há atualização automática do executável nesta versão.

O código de pareamento e os acessos mudam ao reiniciar o programa. Use **Desconectar todos** no PC para revogar os celulares sem fechar o programa. O editor só aceita acesso pelo próprio computador e pelo endereço com chave exibido na janela do StreamCell.

Use em uma rede local confiável: esta versão usa HTTP local, sem criptografia de transporte, e não deve ser exposta à internet ou usada em Wi-Fi público. Não é necessário encaminhar portas no roteador. A conexão ao OBS ocorre apenas no PC por loopback.

Se a configuração estiver danificada, o programa preserva o arquivo e bloqueia novos salvamentos para não perder dados. Feche o programa e restaure config.json.bak. Um backup contendo senha criptografada depende do mesmo usuário Windows; em outro PC, configure a senha novamente. Configurações do protótipo Deck Local não são migradas automaticamente.

## Desenvolvimento e testes

Requisitos: Windows 10/11, .NET Framework 4.8. O aplicativo distribuído é um executável portátil, com a interface embutida, sem Node, Python ou servidor externo para o usuário final. O executável ainda não tem assinatura comercial de código.

Compile com **build.bat**, ou execute no PowerShell:

    .\build.ps1

Saída: build\StreamCell.exe. Para os testes de desenvolvimento, use Node.js 22 ou superior:

    node tests/smoke.mjs
    node tests/obs-integration.cjs

O primeiro teste verifica a fronteira entre PC e celular, validação, pareamento, revogação, privacidade dos payloads e abertura de um executável de teste inofensivo. O segundo usa um OBS simulado para autenticação, frames fragmentados, cenas, mudo, transmissão, gravação, falhas e reconexão. Não aciona uma live real. Testes deixam apenas fixtures dentro de build/tests (ignorado pelo Git).

Opções úteis para desenvolvimento:

    StreamCell.exe --no-browser --port 8766 --data-dir .\dados-de-teste

A porta padrão é 8765. Para publicar uma nova versão, atualize Version e os atributos de versão em Program.cs. O workflow do GitHub compila, testa e publica o executável e o ZIP quando encontra uma versão ainda não lançada na branch main. Pull requests apenas compilam e testam.

## Referências

- [Protocolo oficial OBS WebSocket](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md)
- [Controle remoto oficial do Soundpad](https://www.leppsoft.com/soundpad/help/manual/tutorial/rc/)

StreamCell é um projeto independente, sem afiliação com Elgato, OBS ou Soundpad.
