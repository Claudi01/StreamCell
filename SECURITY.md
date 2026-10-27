# Segurança da versão beta

O StreamCell é um projeto beta. A comunicação entre Android e PC usa HTTP na rede local; mantenha PC e celular em uma rede privada confiável e não compartilhe o código temporário. O servidor aceita conexões somente da rede local, limita tentativas de pareamento e permite revogar celulares pelo PC. Não há cadastro, usuário ou senha do StreamCell.

O deck e as configurações ficam na pasta local do StreamCell. A senha do OBS é protegida pelo Windows para a conta atual. O token de conexão fica no Android e pode ser revogado pelo editor do PC. O app não envia dados a serviços externos.

Para permitir que novas versões do APK sejam instaladas por cima da versão anterior, o repositório público inclui uma chave de assinatura beta e sua senha de demonstração. Essa chave **não prova a autoria do APK**: quem obtiver a chave pode assinar um APK compatível. Instale somente os APKs publicados na Release oficial deste repositório. Antes de distribuir o app amplamente ou incluí-lo em uma loja, substitua essa configuração por uma chave privada guardada como segredo de publicação, fora do repositório.