# Configuration & Externalized Settings Inventory

ZavaQueueBridge relies on a small set of configuration sources: legacy `.NET` application settings, environment-variable overrides, and container build settings. There are no named runtime environments beyond the default configuration plus per-variable environment overrides.

## Configuration Sources

| Source | Type | Path/Location | Notes |
| --- | --- | --- | --- |
| app.config | Application settings | `/tmp/workspace/crgarcia12/mnm-ZavaQueueBridge/app.config` | Defines default RabbitMQ host, credentials, exchange names, poll interval, and file paths |
| Process environment variables | Runtime overrides | Process environment | `Env` and `EnvInt` allow environment variables to override `app.config` values |
| ZavaQueueBridge.csproj | Build configuration | `/tmp/workspace/crgarcia12/mnm-ZavaQueueBridge/ZavaQueueBridge.csproj` | Defines Debug and Release build configurations, framework target, and language version |
| packages.config | Package manifest | `/tmp/workspace/crgarcia12/mnm-ZavaQueueBridge/packages.config` | Pins the RabbitMQ client package version |
| Dockerfile | Container build and runtime defaults | `/tmp/workspace/crgarcia12/mnm-ZavaQueueBridge/Dockerfile` | Builds and runs the application in a Mono 6.12 image |

## Build Profiles

| Profile | Activation | Purpose | Key Dependencies/Plugins |
| --- | --- | --- | --- |
| Debug | Default local build configuration | Produces debug symbols and disables optimization | Legacy MSBuild project settings |
| Release | Manual `/p:Configuration=Release` or Dockerfile build | Produces optimized executable output | Legacy MSBuild project settings |

## Runtime Profiles

| Profile | Activation Method | Config Files | Key Overrides |
| --- | --- | --- | --- |
| Default | Automatic | `app.config` | RabbitMQ connection details, exchange names, poll interval, and file-system paths |
| Environment override | Environment variables present at process start | Process environment plus `app.config` fallback | `RABBITMQ_*`, `FILEDROP_*`, and `WORKER_POLL_INTERVAL_MS` override defaults |

## Properties Inventory

### ZavaQueueBridge

| Property Key | Default | Profiles | Source |
| --- | --- | --- | --- |
| RabbitMQ.Host | `rabbitmq` | Default, Environment override | `app.config`, `RABBITMQ_HOST` |
| RabbitMQ.Port | `5672` | Default, Environment override | `app.config`, `RABBITMQ_PORT` |
| RabbitMQ.VHost | `/zavabank` | Default, Environment override | `app.config`, `RABBITMQ_VHOST` |
| RabbitMQ.User | `zava_app` | Default, Environment override | `app.config`, `RABBITMQ_USER` |
| RabbitMQ.Password | `[MASKED]` | Default, Environment override | `app.config`, `RABBITMQ_PASSWORD` |
| RabbitMQ.FiledropExchange | `filedrop.events` | Default, Environment override | `app.config`, `FILEDROP_EXCHANGE` |
| RabbitMQ.DeadLetterExchange | `zava.dlx` | Default, Environment override | `app.config`, `RABBITMQ_DLX` |
| Worker.PollIntervalMs | `5000` | Default, Environment override | `app.config`, `WORKER_POLL_INTERVAL_MS` |
| FileDrop.Path | `/shared/filedrop` | Default, Environment override | `app.config`, `FILEDROP_PATH` |
| FileDrop.ProcessedPath | `/shared/filedrop/processed` | Default, Environment override | `app.config`, `FILEDROP_PROCESSED_PATH` |
| FileDrop.ErrorPath | `/shared/filedrop/error` | Default, Environment override | `app.config`, `FILEDROP_ERROR_PATH` |

## Startup Parameters & Resource Requirements

| Service | JVM/Runtime Options | Memory | Instance Count |
| --- | --- | --- | --- |
| ZavaQueueBridge | None explicitly configured in repository | Not specified | 1 implied worker process |

## Startup Dependency Chain

1. ZavaQueueBridge loads configuration values from environment variables or `app.config`.
2. The process ensures the drop, processed, and error directories exist before enabling file watching.
3. The application then depends on the shared file system being writable and the RabbitMQ broker being reachable when `PollFiles` opens a connection.
4. There are no explicit health checks, retry loops for broker startup, or orchestration manifests defining readiness dependencies.

## Secrets & Sensitive Configuration

| Secret Reference | Type | Storage (masked) |
| --- | --- | --- |
| RabbitMQ.User | Broker credential | `app.config` default or `RABBITMQ_USER` |
| RabbitMQ.Password | Broker secret | `[MASKED]` in `app.config` default or `RABBITMQ_PASSWORD` |

### Secrets Provisioning Workflow

Secrets are supplied either as checked-in defaults in `app.config` or as environment-variable overrides at deployment time. On startup, the process reads environment variables first and falls back to `app.config`, then reuses those values for every broker connection. No external secret store, managed identity, or secret rotation workflow was detected.

## Feature Flags

| Flag Name | Default | Controlled By |
| --- | --- | --- |
| None detected | N/A | No feature-toggle framework or conditional configuration was found |

## Framework & Runtime Versions

| Component | Version | Source |
| --- | --- | --- |
| .NET Framework target | 4.8 | `ZavaQueueBridge.csproj` |
| C# language version | 7.3 | `ZavaQueueBridge.csproj` |
| MSBuild project schema | ToolsVersion 15.0 | `ZavaQueueBridge.csproj` |
| RabbitMQ.Client | 5.2.0 | `packages.config` |
| Container runtime image | Mono 6.12 | `Dockerfile` |
