# Dragonfire.Outbox.AzureStorageQueue — Sample

End-to-end demo of the **Azure Storage Queue** transport for Dragonfire.Outbox, running entirely on a developer laptop against the **Azurite** local emulator.

```
POST /orders                                                       (you)
   │
   ▼ same SQL transaction
┌──────────────────────┐    ┌──────────────────────────────────────┐
│ AppDbContext.Orders  │    │ OutboxDbContext.OutboxMessages       │
│ INSERT Order         │    │ INSERT OutboxMessage (status=Pending)│
└──────────┬───────────┘    └──────────────┬───────────────────────┘
           └──────── COMMIT ───────────────┘
                            │
                            ▼
              ┌──────────────────────────────┐
              │ OutboxProcessorService       │  background HostedService
              │ ProcessingMode=QueueMediated │
              │  → IMessagePublisher         │  ←── AzureStorageQueuePublisher
              └──────────────┬───────────────┘
                             │ JSON envelope
                             ▼
                ┌──────────────────────────┐
                │ Azurite queue            │  outbox-messages
                │ (Base64-encoded body)    │
                └──────────────┬───────────┘
                               │
                               ▼
                ┌──────────────────────────┐
                │ QueueConsumerService     │  BackgroundService in this app
                │ → log.LogInformation     │
                └──────────────────────────┘
```

The queue-mediated mode is what makes this work: the processor picks up locked outbox rows, hands each one to `IMessagePublisher.PublishAsync` (the AzureStorageQueue connector), and marks the row processed. No webhook subscriptions, no HTTP delivery — just SQL → queue.

---

## Run it

### 1. Start Azurite

Pick whichever flavor you like:

```bash
# Docker (cleanest)
docker run --rm -p 10000:10000 -p 10001:10001 -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite

# OR npm (no Docker required)
npm install -g azurite
azurite --silent
```

Either way Azurite listens on `127.0.0.1:10001` for the Queue service. The connection string `UseDevelopmentStorage=true` resolves to that.

### 2. Start the sample

```bash
cd Dragonfire.Outbox/samples/Dragonfire.Outbox.AzureStorageQueueSample
dotnet run
```

It listens on <http://localhost:5077>. Schemas `app` and `outbox` are created in `(localdb)\MSSQLLocalDB`, database `DragonfireOutboxAsqSample`, on first run.

### 3. Publish events

```bash
# Single order with random data
curl -X POST http://localhost:5077/orders -H "Content-Type: application/json" -d '{}'

# Or specify the values
curl -X POST http://localhost:5077/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"cust-7777","total":42.50,"currency":"EUR"}'

# See what's been written
curl http://localhost:5077/orders
```

### 4. Watch the consumer log

In the `dotnet run` console you'll see, within a couple seconds of each POST:

```
info: Dragonfire.Outbox.AzureStorageQueueSample.Consumer.QueueConsumerService[0]
      Consumed order.created (msg 5e1c…) at 2026-05-07T12:34:56Z: {"orderId":"5e1c…","customerId":"cust-7777","total":42.5,"currency":"EUR","createdAt":"…"}
```

---

## Inspecting the queue directly

If you want to peek at the raw queue (e.g. before the consumer drains it), point [Azure Storage Explorer](https://azure.microsoft.com/features/storage-explorer/) at `Local & Attached → Storage Accounts → (Emulator – Default Ports)`.

You can also use `az` CLI:

```bash
az storage queue list \
  --connection-string "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;"
```

---

## Notes / gotchas

- **`ProcessingMode = QueueMediated`** is required. The connector registers `IMessagePublisher`, but the processor only calls it when the option is set. Without it, the processor falls back to `IWebhookDeliverer` and your queue stays empty.
- **No subscriptions table is needed.** Unlike the webhook delivery path, queue-mediated mode ignores `WebhookSubscription` entirely — there is exactly one queue per processor.
- **At-least-once delivery.** The processor publishes, *then* marks the row processed. A crash between the two means the same message arrives in Azure twice. Your consumers must be idempotent on `MessageId`.
- **Visibility timeout = 0** in `Program.cs`. Messages are immediately visible to receivers, since the durable copy lives in the outbox table — there's no need for a long invisibility window. If you crank this up, the consumer will wait that long after each enqueue before seeing the message.
- **Queue name is fixed** — every event type goes to the same queue. If you want per-event-type fan-out, add a routing layer in front of the connector or implement `IMessagePublisher` yourself.

---

## Switching to a real Storage account

Replace the connection string in `appsettings.json`:

```json
"AzureStorageQueue": {
  "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
  "QueueName": "orders"
}
```

Nothing else changes — the connector talks to Azurite and the real Azure Storage service through identical SDK calls.
