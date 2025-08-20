# Guia de Arquitetura — Rent.Motorcycle (Domínios Ricos, Regras de Locação e Mensageria)


---

## 1) Visão Geral e Camadas

**Projeto** organizado em três camadas principais:

* **API** (`Rent.Motorcycle.API`) — Endpoints HTTP minimal API; expõe DTOs, valida entradas superficiais e **orquestra** chamadas ao domínio/infra.
* **Domínio** (`Rent.Motorcycle.Domain`) — Onde ficam **entidades ricas**, **Value Objects**, **Enums** e **regras de negócio**. *Não conhece* EF Core, RabbitMQ ou detalhes de I/O.
* **Infra** (`Rent.Motorcycle.Infra`) — Integrações de **persistência** (EF Core/Npgsql), **mensageria** (RabbitMQ) e **armazenamento** (disco para imagem de CNH). Também registra serviços via DI.

> **Fluxo**: Request → API (DTO/validação superficial) → Domínio (regras, invariantes, cálculos) → Infra (gravar/consultar, publicar evento, armazenar arquivo) → Response (DTO/VM).

---

## 2) Domínios, Agregados e Relações

### 2.1 Entidades Principais

* **`DeliveryRider`** (Entregador)

  * Atributos: `Id`, `CNPJ`, `Name`, `BirthDate`, `CNH` (Value Object), metadados (`Active`, `CreatedAt`, etc.).
  * Responsabilidades:

    * **Cadastro** de dados do entregador.
    * **Elegibilidade** por CNH (ex.: motos exigem `A` ou `APlusB`).
    * **Atualização da imagem da CNH** (caminho salvo após upload via Infra/Storage).
  * Decisões:

    * **Normalização de CNPJ** (só dígitos) e validações básicas.

* **`Motorcycle`** (Moto)

  * Atributos: `Id`, `Year`, `Model`, `Plate`, `HasRentals` + metadados.
  * Responsabilidades:

    * **Criação** com validação de placa **Mercosul** (regex), faixa de ano, etc.
    * **Renomear modelo** e **trocar placa** com invariantes (não aceitar vazio; normalizar; marcar atualização).
    * Flags de disponibilidade (`HasRentals`).

* **`Rental`** (Locação)

  * Atributos: `Id`, `IdMotorcycle`, `IdDeliveryRider`, `StartDate`, `ExpectedEndDate`, `EndDate` (retorno), `Plan`, `Total`, `Active`.
  * Propriedades derivadas: `Identifier` (formato `locacao{Id}`), `DailyPrice` (por plano), `LateExtraDailyFee` (R\$ 50), `ReturnDate`.
  * Responsabilidades:

    * **Criação** da locação com plano definido.
    * **Cálculo de preço** por meio de `CalculatePreview(returnDate)` retornando um **Value Object** (`PriceBreakdown`).

* **`Admin`**

  * Mantém uma **coleção interna de motos**; operações administrativas como adicionar/trocar placa/renomear/remoção com invariantes de unicidade.
  * Define um *record* interno `MotorcycleRegistered` (conceito de evento de domínio); ver seção de Mensageria para o evento publicado na API.

### 2.2 Value Objects e Enums

* **`CNH`** (VO): Tipo (`A`, `B`, `APlusB`), número e URL da imagem. É **imutável** após criação; valida número.
* **`PriceBreakdown`** (VO): `UsedDays`, `UnusedDays`, `ExtraDays`, `DailyPrice`, `BaseValue`, `Penalty`, `Extras`, `Total`. É o **contrato** de cálculo do domínio, independente de API.
* **Enums**:

  * `CNHType`: `A`, `B`, `APlusB`.
  * `RentalPlan`: `Days7`, `Days15`, `Days30`, `Days45`, `Days50`.

### 2.3 Relações entre Agregados

* **`Rental` -> `DeliveryRider` e `Motorcycle`**: a locação **armazena apenas os IDs** (`IdDeliveryRider`, `IdMotorcycle`). Entregador e moto têm **ciclo de vida próprio**; a `Rental` não “contém” esses objetos.

* **Integridade referencial (EF Core)**: Foreign Keys usam `DeleteBehavior.Restrict`. Ou seja, **não é possível excluir** um entregador ou uma moto enquanto existir locação que os referencie.

* **`Motorcycle` e `Admin`**: `Motorcycle` é **raiz de agregado** independente e garante suas invariantes (placa, ano, etc.). `Admin` **apenas organiza/consulta** uma coleção de motos para fins administrativos; **não** gerencia o ciclo de vida delas nem provoca deleção em cascata.

**Leitura mental do modelo**

```
Rental --(IdDeliveryRider)--> DeliveryRider   (agregado independente)
   |
   +--(IdMotorcycle)-----> Motorcycle         (agregado independente)
Admin  -- lista/organiza --> [Motorcycle]*    (sem posse/cascata)
```

**Consultas**: quando precisar exibir dados da moto/entregador junto com a locação, **carregue por join/Include**; a associação no domínio é por ID.

### 2.4 Diagrama de Domínio
![Diagrama de Domínio](images/diagrama_de_dominio.svg)


---

## 3) Domínio Rico (invariantes + comportamentos)

A proposta do projeto é **domínio rico**, isto é, as entidades **guardam suas próprias regras** e oferecem **métodos** que preservam invariantes:

* **Validações na criação** (`Create`) e nas mutações (ex.: `Motorcycle.ChangePlate`, `DeliveryRider.UpdateCNHImage`).
* **Regras encapsuladas**: formatação/regex de placa, normalização de CNPJ, checagens de elegibilidade da CNH.
* **Cálculo de preço** fica **no domínio** (`Rental.CalculatePreview`) produzindo um VO independente de infraestrutura.


---

## 4) Regras de Negócio da Locação e Cálculo de Preço

### 4.1 Planos, preços e penalidades

* **Preço por diária** (por plano):

  * 7 dias → **R\$ 30**
  * 15 dias → **R\$ 28**
  * 30 dias → **R\$ 22**
  * 45 dias → **R\$ 20**
  * 50 dias → **R\$ 18**
* **Multa por devolução antecipada** (somente para 7 e 15 dias):

  * Plano 7d → **20%** sobre **dias não usados × diária**
  * Plano 15d → **40%** sobre **dias não usados × diária**
  * Demais planos → **0%** (não há multa definida)
* **Diária extra por atraso**: **R\$ 50,00/dia** (aplicada além do valor base já usado do plano).

### 4.2 Convenções de contagem

* Contagem por **dias de calendário (inclusivo)**: de *01* a *07* = **7 dias**.
* `used_days` = dias inclusivos entre início e `returnDate`, **limitado** ao tamanho do plano.
* `unused_days` = `plan_days - used_days` (mínimo 0).
* `extra_days` = dias **após** `expectedEndDate`.

### 4.3 Fórmulas (síntese)

* `valor_base = used_days × daily_price`
    * **Se retorno ≤ previsto**:
        * `multa = penalty_rate × (unused_days × daily_price)`
    * **Se retorno > previsto**:
        * `extras = extra_days × 50`
* `total = valor_base + multa + extras`

### 4.4 Resultado do cálculo

O domínio retorna um **`PriceBreakdown`** já com `usedDays`, `unusedDays`, `extraDays`, `dailyPrice`, `baseValue`, `penalty`, `extras` e `total`. A API apenas **projeta** isso para a VM (`PreviewVm`).

---

## 5) API HTTP — Rotas e Contratos

> A API usa *Minimal APIs* e retorna VMs/DTOs em Português. Algumas respostas incluem exemplos no Swagger.

### 5.1 Entregadores (`/entregadores`)

* **POST** `/entregadores` — Cadastra entregador (inclui dados de CNH).
* **PUT** `/entregadores/{id}/cnh` — Atualiza **imagem da CNH** via Base64; a Infra grava no disco e salva o caminho no domínio.
* **GET/PUT** (conforme implementado) para consulta/alterações básicas.

### 5.2 Motos (`/motos`)

* **POST** `/motos` — Cria moto, valida placa (Mercosul), e **publica evento** `motorcycle.registered` (ver Mensageria).
* **GET** `/motos/{id}` e operações administrativas (renomear/trocar placa) conforme exposto pela API.

### 5.3 Locações (`/locacao`)

* **POST** `/locacao` — Cria locação a partir de `riderId`, `motorcycleId`, `startDate`, `endDate`, `expectedEndDate` e `plan`.
* **GET** `/locacao/{id|locacao{id}}` — Retorna detalhes da locação.
* **PUT** `/locacao/{id|locacao{id}}/devolucao` — Recebe `{ "data_retorno": <ISO8601 UTC> }` e retorna `PreviewVm` com o cálculo do total.

**Convenções de ID**: As rotas de locação aceitam `123` **ou** `locacao123`.

**Códigos de status**: `201` (criação), `200` (OK), `400` (dados inválidos), `404` (não encontrado). As mensagens de erro retornam `{ "mensagem": "..." }`.

---

## 6) Persistência (Infra/Data)

* **DbContext** expõe `DbSet<DeliveryRider>`, `DbSet<Motorcycle>` e `DbSet<Rental>`.
* **Relacionamentos (EF Core)**

  * `Rental` → `DeliveryRider` (FK `IdDeliveryRider`) com `DeleteBehavior.Restrict` — você **não pode apagar** um entregador se existir locação apontando para ele.
  * `Rental` → `Motorcycle` (FK `IdMotorcycle`) com `DeleteBehavior.Restrict` — você **não pode apagar** uma moto se existir locação ligada a ela.
* **Migrações** criam as tabelas de Admins, Entregadores, Motos, Locações e a tabela de eventos `moto_registered_events`.

---

## 7) Mensageria com RabbitMQ

### 7.1 Publicação (Producer)
No *sucesso* de operações que interessam a outros serviços. Ex.: ao criar uma moto, publicamos motorcycle.registered.

**RabbitMqEventBus** serializa o payload em JSON e chama `BasicPublish` na exchange `rent.events` com a *routing key* do evento.

Propriedades usadas: `ContentType = application/json`, `DeliveryMode = 2` (mensagem persistente), `MessageId`, `CorrelationId` e `Timestamp`.

  ```json
  {
    "MotorcycleId": "...",
    "Year": 2025,
    "Model": "...",
    "Plate": "ABC1D23",
    "OccurredAt": "2025-08-18T12:34:56Z"
  }
  ```

### 7.2 Consumo (Consumer)

**BackgroundService** (`MotorcycleRegisteredConsumerService`) cria a conexão `/IModel`, declara a fila  e faz o binding com a routing key `motorcycle.registered`.
* Fluxo por mensagem:
  * Deserializar JSON → objeto.
  * Processar (regra/efeito colateral).
  * Ack (BasicAck) em caso de sucesso. Em falha não recuperável:  BasicNack(requeue:false) para enviar à DLQ
> O serviço usa as chaves do `appsettings` (seção `RabbitMq`) para host, porta, vhost, usuário e senha.

### 7.3 Configuração

* **`appsettings.Docker.json`** define:

  ```json
  {
    "RabbitMq": {
      "Host": "rabbitmq",
      "Port": 5672,
      "VirtualHost": "/",
      "User": "app",
      "Password": "app",
      "Exchange": "rent.events",
      "Enabled": true
    }
  }
  ```
* **DI/Infra**: registra `IEventBus` → `RabbitMqEventBus` e o `HostedService` consumidor.

---

## 8) Armazenamento de Arquivos (CNH)

* Upload via **Base64** (`/entregadores/{id}/cnh`), que é decodificado e salvo em disco por `DiskStorageService`.
* Caminho final é persistido no domínio (`CNH.CnhImageUrl`).
* **Configuração**: variável `STORAGE_ROOT` (Docker) ou caminho padrão na pasta `storage` do app.

## 9) Como rodar em Docker
**Pré-requisitos**: Docker e Docker Compose instalados.
##### Para subir a stack
```
docker compose up --build
```
##### Acessos úteis

* Swagger da API: http://localhost:8080
* RabbitMQ Management: http://localhost:15672
