# Fluxo Integrado: Identity Federada Google Play Games e Relay Server

Este documento apresenta a especificação e o guia de utilização dos serviços de autenticação, identidade federada com Google Play Games Services e gerenciamento de salas com o Relay Server no MemoAna backend.

---

## Visão Geral da Arquitetura

O backend do MemoAna adota uma arquitetura em camadas estruturada com ASP.NET Core (.NET 10), ASP.NET Core Identity (com persistência SQLite / EF Core), validação via FluentValidation, orquestração desacoplada por Mediator (`Mediator.Abstractions` e `Mediator.SourceGenerator`), autenticação JWT Bearer com suporte a revogação em memória, e servidor de transporte UDP de baixa latência baseado em `LiteNetLib` para o Relay de partidas multiplayer.

```mermaid
flowchart TD
    subgraph Cliente MAUI
        A[App MemoAna Android]
        B[Google Play Games SDK]
    end

    subgraph Backend ASP.NET Core
        C[AuthController]
        D[Mediator Pipeline]
        E[GooglePlayGamesAuthenticationService]
        F[GooglePlayGamesClient]
        G[IdentityService]
        H[ASP.NET Identity + SQLite]
        I[JwtTokenService]
        J[RelayServerController]
        K[RelayService UDP / LiteNetLib]
    end

    subgraph Google Cloud
        L[Google OAuth2 Token Endpoint]
        M[Google Play Games API v1]
    end

    A -->|1. Solicita login Play Games| B
    B -->|2. Retorna Server Auth Code| A
    A -->|3. POST /auth/login/google-play-games| C
    C --> D --> E
    E -->|4. Troca code por tokens| F -->|oauth2.googleapis.com| L
    E -->|5. Consulta perfil do jogador| F -->|games.googleapis.com| M
    E -->|6. Localiza / Cria identidade| G --> H
    G -->|7. Emite Access e Refresh Token| I
    C -->|8. Retorna TokenResponse| A
    A -->|9. POST /relay/rooms| J --> K
```

---

## Configuração de Ambiente e Credenciais

As configurações do serviço de autenticação Google Play Games são fornecidas através de variáveis de ambiente (suportando arquivos `.env` em desenvolvimento através do `DotEnvLoader` e variáveis de ambiente injetadas em contêineres Docker em produção).

| Variável | Descrição | Uso |
| :--- | :--- | :--- |
| `GOOGLE_PLAY_GAMES_APPLICATION_ID` | Application ID do Play Games Services | Identificador do jogo no Google Play Console. |
| `GOOGLE_PLAY_GAMES_ANDROID_CLIENT_ID` | OAuth 2.0 Client ID Android | Configurado no SDK Android do cliente MAUI. |
| `GOOGLE_PLAY_GAMES_WEB_CLIENT_ID` | OAuth 2.0 Client ID Web | Usado pelo cliente como audiência para obter o Server Auth Code e pelo backend para a troca de código. |
| `GOOGLE_PLAY_GAMES_WEB_CLIENT_SECRET` | OAuth 2.0 Client Secret Web | **Exclusivo do backend**. Utilizado na validação e troca do código de autorização. Nunca compartilhado com o cliente nem exposto em logs. |
| `GOOGLE_PLAY_GAMES_AUTHORITY` | Autoridade OpenID Connect (`https://accounts.google.com`) | Validação de autoridade emissora. |
| `GOOGLE_PLAY_GAMES_AUDIENCE` | Audiência esperada | Validada nas credenciais e nos ID tokens Google. |
| `GOOGLE_PLAY_GAMES_VALID_AUDIENCE` | Audiência complementar de validação | Audiência aceita na validação. |
| `GOOGLE_PLAY_GAMES_REDIRECT_URI` | URI de redirecionamento para o fluxo OAuth | **Atenção:** No fluxo de Server Authorization Code do Play Games Services em clientes Android nativos, o valor correto para a troca de código é uma string vazia `""`. Caso o Google Cloud Console exija um valor explicitamente configurado, ele deve ser informado nesta variável. |

> [!IMPORTANT]
> Em produção ou em pipelines de CI, nunca inclua o `.env` real nem credenciais confidenciais no repositório. O `.gitignore` protege o arquivo `.env`.

---

## Etapa 1 — Registro Tradicional

Permite que um usuário crie uma conta local diretamente no MemoAna utilizando email e senha.

### Endpoint
`POST /api/v1/auth/register`  
Autenticação: Anônimo (`[AllowAnonymous]`)

### Request
```http
POST /api/v1/auth/register HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "email": "jogador@exemplo.com",
  "password": "SenhaForte123!"
}
```

### Response (200 OK)
```http
HTTP/1.1 200 OK
```

### Tratamento de Erros
* **400 Bad Request — Erros de validação ou email duplicado:**
```json
{
  "succeeded": false,
  "errors": [
    "The email is already in use"
  ]
}
```
* **422 Unprocessable Entity — Requisitos de complexidade de senha:**
A senha deve ter no mínimo 8 caracteres, contendo pelo menos uma letra maiúscula, uma letra minúscula, um dígito numérico e um caractere especial.

---

## Etapa 2 — Login Tradicional

Autentica as credenciais locais do usuário e emite os JWTs do MemoAna.

### Endpoint
`POST /api/v1/auth/login`  
Autenticação: Anônimo (`[AllowAnonymous]`)

### Request
```http
POST /api/v1/auth/login HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "email": "jogador@exemplo.com",
  "password": "SenhaForte123!",
  "twoFactorCode": null,
  "twoFactorRecoveryCode": null
}
```

### Response (200 OK)
```json
{
  "tokenType": "Bearer",
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 900,
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

### Tratamento de Erros
* **401 Unauthorized — Credenciais inválidas ou 2FA pendente:**
```json
{
  "value": null,
  "message": "Invalid credentials.",
  "success": false
}
```

---

## Etapa 3 — Autenticação Google Play Games

Esta etapa implementa a federação de identidade por meio do Google Play Games Services v2.

### Responsabilidade do Cliente (.NET MAUI / Android)
1. O cliente MAUI inicializa o SDK do Google Play Games Services no dispositivo Android.
2. O jogador realiza o login nativo no Play Games no dispositivo.
3. O cliente MAUI invoca `PlayGames.getGamesServerAuthCodeClient().getGamesServerAuthCode(webClientId, forceRefreshToken)` informando o `GOOGLE_PLAY_GAMES_WEB_CLIENT_ID`.
4. O SDK do Google emite um Server Authorization Code de uso único.
5. O cliente envia esse código no corpo da requisição HTTP ao backend.

### Processamento pelo Backend
1. O backend recebe o `serverAuthCode` via `POST /api/v1/auth/login/google-play-games`.
2. O `GooglePlayGamesAuthenticationService` utiliza o `GoogleAuthorizationCodeFlow` com o Web Client ID e Web Client Secret para trocar o código com `https://oauth2.googleapis.com/token`.
3. O `redirect_uri` enviado para a troca de código é `""` (string vazia), conforme especificação do Google Play Games para Android, evitando redirects HTTP fictícios.
4. O backend recebe o access token e ID token do Google e consulta o perfil oficial do jogador via Google Play Games API v1 (`GamesService.Players.Get("me")`).
5. O identificador estável do jogador (`PlayerId`, ex: `g0123456789`) é obtido e utilizado como `providerKey`.
6. O backend localiza o usuário pelo par `(LoginProvider: "GooglePlayGames", ProviderKey: player.PlayerId)` via ASP.NET Identity.
7. Se não existir, cria um novo usuário local armazenando os dados oficiais do perfil:
   * `PlayerId`: identificador único do jogador;
   * `DisplayName`: nome público / gamer tag no Play Games;
   * `AvatarUrl`: URL do avatar ou imagem de perfil oficial;
   * `Email`: email verificado caso concedido no escopo OpenID (caso ausente, o campo permanece `null`, **sem criação de emails sintéticos inválidos** como `@external.local`);
   * `FirstName` e `SurName`: nome e sobrenome quando disponibilizados pelo perfil Google.
8. O backend emite seu próprio par de tokens JWT (Access Token de 15 minutos e Refresh Token de 14 dias).

### Endpoint
`POST /api/v1/auth/login/google-play-games`  
Autenticação: Anônimo (`[AllowAnonymous]`)

### Request
```http
POST /api/v1/auth/login/google-play-games HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "serverAuthCode": "4/0AeanS0aB...exemplo_auth_code_google...",
  "redirectUri": ""
}
```

### Response (200 OK)
```json
{
  "tokenType": "Bearer",
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 900,
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

### Tratamento de Erros
* **401 Unauthorized — Código inválido, expirado ou falha de comunicação:**
```json
{
  "value": null,
  "message": "Google Play Games authentication failed.",
  "success": false
}
```

---

## Etapa 4 — Associação de Identidade Externa (Account Linking)

Todo usuário registrado tradicionalmente (por email e senha) pode associar sua conta Google Play Games para permitir futuros logins biométricos/automáticos pelo Play Games.

### Requisitos de Segurança
* **Usuário Autenticado:** Requer cabeçalho `Authorization: Bearer <accessToken>`.
* **Validação Prévia da Identidade Externa:** O backend valida o Server Auth Code junto à API Google antes de registrar a vinculação.
* **Prevenção de Colisão:** Caso a mesma conta Google Play Games já esteja vinculada a outro usuário do MemoAna, a operação é rejeitada.
* **Prevenção de Substituição Silenciosa:** Se a conta atual já estiver vinculada a uma identidade Google diferente, a operação falha explicitamente.
* **Persistência Oficial via ASP.NET Identity:** A associação é gravada via `UserManager.AddLoginAsync(user, new UserLoginInfo("GooglePlayGames", playerId, "GooglePlayGames"))`.

### Endpoint
`POST /api/v1/auth/manage/link/google-play-games`  
Autenticação: `Bearer <accessToken>` (`[Authorize]`)

### Request
```http
POST /api/v1/auth/manage/link/google-play-games HTTP/1.1
Host: localhost:5001
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "serverAuthCode": "4/0AeanS0bC...novo_auth_code...",
  "redirectUri": ""
}
```

### Response (200 OK)
```http
HTTP/1.1 200 OK
```

### Tratamento de Erros
* **400 Bad Request — Identidade já vinculada a outro usuário:**
```json
{
  "succeeded": false,
  "errors": [
    "This external identity is already linked to another user account."
  ]
}
```
* **400 Bad Request — Substituição de provedor já existente:**
```json
{
  "succeeded": false,
  "errors": [
    "An account from this provider is already linked to this user."
  ]
}
```
* **401 Unauthorized — Token MemoAna ausente ou expirado:**
```http
HTTP/1.1 401 Unauthorized
```

---

## Etapa 5 — Consulta de Identidade

Retorna os dados do perfil do usuário autenticado no MemoAna.

### Endpoint
`GET /api/v1/auth/manage/info`  
Autenticação: `Bearer <accessToken>` (`[Authorize]`)

### Request
```http
GET /api/v1/auth/manage/info HTTP/1.1
Host: localhost:5001
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### Response (200 OK)
```json
{
  "email": "jogador@exemplo.com",
  "isEmailConfirmed": true,
  "displayName": "SuperPlayerTag",
  "playerId": "g0123456789",
  "avatarUrl": "https://play.google.com/avatar/sample.png",
  "firstName": "Ana",
  "surName": "Silva"
}
```

---

## Etapa 6 — Atualização de Identidade

Permite a alteração de email e senha mediante confirmação da senha atual.

### Endpoint
`POST /api/v1/auth/manage/info`  
Autenticação: `Bearer <accessToken>` (`[Authorize]`)

### Request
```http
POST /api/v1/auth/manage/info HTTP/1.1
Host: localhost:5001
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "newEmail": "novoemail@exemplo.com",
  "newPassword": "NovaSenhaForte123!",
  "oldPassword": "SenhaForte123!"
}
```

### Response (200 OK)
```http
HTTP/1.1 200 OK
```

---

## Etapa 7 — Refresh Token

Renova o par de tokens JWT antes da expiração do access token. O refresh token utilizado é revogado imediatamente para impedir ataques de repetição (replay attacks).

### Endpoint
`POST /api/v1/auth/refresh`  
Autenticação: Anônimo (`[AllowAnonymous]`)

### Request
```http
POST /api/v1/auth/refresh HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

### Response (200 OK)
```json
{
  "tokenType": "Bearer",
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...novoAccess...",
  "expiresIn": 900,
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...novoRefresh..."
}
```

---

## Etapa 8 — Revoke Token

Revoga o access token atual, impedindo seu uso futuro antes da sua expiração natural.

### Endpoint
`POST /api/v1/auth/revoke`  
Autenticação: `Bearer <accessToken>` (`[Authorize]`)

### Request
```http
POST /api/v1/auth/revoke HTTP/1.1
Host: localhost:5001
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### Response (200 OK)
```http
HTTP/1.1 200 OK
```

---

## Etapa 9 — Criação de Room no Relay

Cria uma sala no Relay Server para que os jogadores possam se conectar via UDP.

> [!NOTE]
> Conforme as regras da feature, os endpoints de Relay operam nesta etapa sem exigência de cabeçalho `[Authorize]`. A autenticação nos endpoints de Relay será adicionada em uma feature posterior.

### Endpoint
`POST /api/v1/relay/rooms`

### Request
```http
POST /api/v1/relay/rooms HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "playerId": "g0123456789",
  "roomName": "Sala dos Campeões",
  "password": "sala-secreta",
  "theme": "Animais",
  "difficulty": 2,
  "playerName": "SuperPlayerTag"
}
```

### Response (201 Created)
```json
{
  "roomId": "a1b2c3d4e5f67890123456789abcdef0",
  "roomName": "Sala dos Campeões",
  "theme": "Animais",
  "difficulty": 2,
  "requiresPassword": true,
  "relayPort": 35680,
  "connectionToken": "f0e1d2c3b4a5968778695a4b3c2d1e0f"
}
```

---

## Etapa 10 — Listagem de Rooms

Consulta as salas ativas disponíveis para ingresso de jogadores convidados.

### Endpoint
`GET /api/v1/relay/rooms`

### Request
```http
GET /api/v1/relay/rooms HTTP/1.1
Host: localhost:5001
```

### Response (200 OK)
```json
[
  {
    "roomId": "a1b2c3d4e5f67890123456789abcdef0",
    "roomName": "Sala dos Campeões",
    "hostPlayerName": "SuperPlayerTag",
    "theme": "Animais",
    "difficulty": 2,
    "requiresPassword": true,
    "createdAt": "2026-11-02T16:00:00Z"
  }
]
```

### Ingresso na Sala (Guest)
Para ingressar na sala:
```http
POST /api/v1/relay/rooms/a1b2c3d4e5f67890123456789abcdef0/join HTTP/1.1
Host: localhost:5001
Content-Type: application/json

{
  "playerId": "guest_player_999",
  "password": "sala-secreta",
  "playerName": "Desafiante"
}
```

Resposta de ingresso (200 OK):
```json
{
  "roomId": "a1b2c3d4e5f67890123456789abcdef0",
  "roomName": "Sala dos Campeões",
  "theme": "Animais",
  "difficulty": 2,
  "relayPort": 35680,
  "connectionToken": "9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d"
}
```

---

## Etapa 11 — Conexão UDP ao Relay Server

Com o `relayPort` e o `connectionToken` obtidos na criação (Host) ou no ingresso (Guest), o cliente .NET MAUI estabelece a conexão de rede em tempo real utilizando a biblioteca `LiteNetLib`.

### Parâmetros de Conexão UDP
* **Endereço do Servidor:** IP ou hostname do backend MemoAna (ex: `127.0.0.1` ou `relay.memoana.local`).
* **Porta UDP:** Valor de `relayPort` retornado pelo backend (padrão: `35680`).
* **Dados de Conexão (`ConnectionRequest`):** String contendo o `connectionToken`.

### Exemplo de Conexão no Cliente MAUI com LiteNetLib
```csharp
var listener = new EventBasedNetListener();
var netManager = new NetManager(listener);
netManager.Start();

// Envia o connectionToken recebido da API HTTP na solicitação de conexão
var writer = new NetDataWriter();
writer.Put(connectionToken);

NetPeer peer = netManager.Connect(serverHost, relayPort, writer);
```

### Comportamento no Servidor
1. O servidor recebe o `connectionToken` em `ConnectionRequestEvent`.
2. O token identifica a sala e a função do jogador (Host ou Guest).
3. Uma vez que ambos os jogadores estejam conectados, as mensagens de rede de virada de cartas, sincronização de turnos e pontuação são retransmitidas bidirecionalmente pelo servidor com baixa latência.

