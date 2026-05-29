# Deployment

This guide covers the two supported deployment paths for this repo:

1. Azure Container Apps through Aspire
2. Docker Compose for local container execution

## Azure Container Apps (Aspire)

### Prerequisites

- Aspire CLI installed: <https://aspire.dev/get-started/install-cli/>
- Azure CLI installed and signed in
- Azure subscription and permission to create resource groups, ACR, and Container Apps
- Production configuration available through environment variables or a checked-in `.env`

### 1. Add Azure support

```bash
aspire add azure-appcontainers
```

### 2. Deploy

```bash
aspire deploy
```

Aspire provisions the Azure Container Apps environment, Azure Container Registry, managed identity, and the container resources described by `apphost.cs`.

### 3. Inspect or remove the deployment

```bash
aspire describe
aspire destroy
```

## Docker Compose

### Prerequisites

- Docker Desktop or Docker Engine with Compose v2
- A `.env` file copied from `.env.example`

### Build and run

```bash
docker compose up --build
```

Useful follow-up commands:

```bash
docker compose ps
docker compose logs -f
docker compose down
```

### Service endpoints

- Web UI: <http://localhost:5000>
- MAF Action Agent: <http://localhost:5055/health>
- NeMo Agent card: <http://localhost:8088/.well-known/agent-card.json>

### Container wiring

- `nemo-agent` listens on `0.0.0.0:8088`
- `maf-agent` listens on `0.0.0.0:5055`
- `web-ui` listens on `0.0.0.0:5000`
- `NEMO_A2A_ENDPOINT` points to `http://nemo-agent:8088`
- `MAF_AGENT_ENDPOINT` points to `http://maf-agent:5055`
- `NEMO_WARMUP_ENABLED` stays disabled inside containers
- OTEL traffic goes to the local collector at `http://otel-collector:4317`

### Observability

The compose stack includes `docker/otel-collector-config.yml`, which receives OTLP traffic from all services and prints trace data to the container logs.

### Troubleshooting

```bash
docker compose logs nemo-agent
docker compose logs maf-agent
docker compose logs web-ui
curl http://localhost:5000/health
curl http://localhost:5055/health
```

## Next steps

- See [Configuration](./CONFIGURATION.md) for environment setup
- See [Manual Startup](./MANUAL-STARTUP.md) for non-container development
- See [Testing](./TESTING.md) for validation commands
- See [Architecture](../README.md#-system-architecture) for the overall system design
