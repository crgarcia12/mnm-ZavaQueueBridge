# Configuration & Externalized Settings Inventory

Configuration is centralized in `app.config` with environment variable overrides at runtime. Build and runtime behavior is minimal and oriented around a single worker process.

## Configuration Sources

| Source | Type | Path/Location | Notes |
|---|---|---|---|
| app.config | Application settings | `/tmp/workspace/crgarcia12/mnm-ZavaQueueBridge/app.config` | Default RabbitMQ, worker, and file drop values |
| Environment variables | Runtime overrides | Process environment | `Env()` method prioritizes env vars over app settings |
| Dockerfile | Build/runtime container config | `/tmp/workspace/crgarcia12/mnm-ZavaQueueBridge/Dockerfile` | Defines Mono base image and build commands |
| ZavaQueueBridge.csproj | Build config | `/tmp/workspace/crgarcia12/mnm-ZavaQueueBridge/ZavaQueueBridge.csproj` | Debug/Release configuration and target framework |

## Build Profiles

| Profile | Activation | Purpose | Key Dependencies/Plugins |
|---|---|---|---|
| Debug | `Configuration=Debug` (default) | Local debugging build with symbols | .NET Framework references + RabbitMQ.Client |
| Release | `Configuration=Release` | Optimized production-style build | Same dependency graph, optimized compilation |

## Runtime Profiles

| Profile | Activation Method | Config Files | Key Overrides |
|---|---|---|---|
| Default | No explicit profile mechanism | `app.config` | Uses `RabbitMQ.*`, `Worker.*`, `FileDrop.*` defaults |
| Environment-driven | Set environment variables | `app.config` + env vars | `RABBITMQ_*`, `FILEDROP_*`, `WORKER_POLL_INTERVAL_MS` override defaults |

## Properties Inventory

| Property Key | Default | Profiles | Source |
|---|---|---|---|
| RabbitMQ.Host | rabbitmq | Default, env override | app.config / `RABBITMQ_HOST` |
| RabbitMQ.Port | 5672 | Default, env override | app.config / `RABBITMQ_PORT` |
| RabbitMQ.VHost | /zavabank | Default, env override | app.config / `RABBITMQ_VHOST` |
| RabbitMQ.User | zava_app | Default, env override | app.config / `RABBITMQ_USER` |
| RabbitMQ.Password | [MASKED] | Default, env override | app.config / `RABBITMQ_PASSWORD` |
| RabbitMQ.FiledropExchange | filedrop.events | Default, env override | app.config / `FILEDROP_EXCHANGE` |
| RabbitMQ.DeadLetterExchange | zava.dlx | Default, env override | app.config / `RABBITMQ_DLX` |
| Worker.PollIntervalMs | 5000 | Default, env override | app.config / `WORKER_POLL_INTERVAL_MS` |
| FileDrop.Path | /shared/filedrop | Default, env override | app.config / `FILEDROP_PATH` |
| FileDrop.ProcessedPath | /shared/filedrop/processed | Default, env override | app.config / `FILEDROP_PROCESSED_PATH` |
| FileDrop.ErrorPath | /shared/filedrop/error | Default, env override | app.config / `FILEDROP_ERROR_PATH` |

## Startup Parameters & Resource Requirements

| Service | JVM/Runtime Options | Memory | Instance Count |
|---|---|---|---|
| ZavaQueueBridge | No explicit runtime options detected | Not explicitly configured | 1 process expected |

## Startup Dependency Chain

1. ZavaQueueBridge process starts and loads configuration.
2. Local directories are ensured (`drop`, `processed`, `error`).
3. RabbitMQ connection is created per poll cycle before publishing.
4. File processing requires RabbitMQ broker availability for successful message publication.

## Secrets & Sensitive Configuration

| Secret Reference | Type | Storage (masked) |
|---|---|---|
| RabbitMQ.Password / `RABBITMQ_PASSWORD` | Broker credential | app.config value (masked) or environment variable |

### Secrets Provisioning Workflow

Secrets are provided either directly in `app.config` defaults or injected through environment variables at process startup. No external vault integration, managed identity flow, or automated secret rotation mechanism is defined in this repository.

## Feature Flags

| Flag Name | Default | Controlled By |
|---|---|---|
| None detected | N/A | N/A |

## Framework & Runtime Versions

| Component | Version | Source |
|---|---|---|
| .NET Framework | 4.8 | `TargetFrameworkVersion` in `.csproj` |
| C# Language | 7.3 | `LangVersion` in `.csproj` |
| RabbitMQ.Client | 5.2.0 | `packages.config` |
| Mono base image | 6.12 | Dockerfile |
