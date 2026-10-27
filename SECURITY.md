# Segurança da versão beta

O StreamCell é um projeto beta. A comunicação entre Android e PC usa HTTP na rede local do usuário; use uma rede privada confiável e mantenha o código de conexão privado. O servidor do PC valida que as conexões vêm de uma rede local e exige uma conta local além do código temporário.

As contas locais guardam hashes PBKDF2 no computador. A senha do OBS usa a proteção de dados do Windows para o usuário atual. O aplicativo não envia essas informações a serviços externos.

Para permitir que novas versões do APK sejam instaladas por cima da versão anterior, o repositório público inclui uma chave de assinatura beta e sua senha de demonstração. Essa chave **não prova a autoria do APK**: quem obtiver a chave pode assinar um APK compatível. Instale somente os APKs publicados na Release oficial deste repositório. Antes de distribuir o app amplamente ou incluí-lo em uma loja, substitua essa configuração por uma chave privada guardada como segredo de publicação, fora do repositório.