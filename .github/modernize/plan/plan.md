# Modernization Plan: Zava Queue Bridge Azure Modernization

**Project**: ZavaQueueBridge

---

## Technical Framework

- **Language**: C# (.NET Framework 4.8)
- **Framework**: .NET Framework console worker application
- **Build Tool**: NuGet + MSBuild
- **Database**: None (file-based ingestion workflow)
- **Key Dependencies**: RabbitMQ.Client, System.Configuration

---

## Overview

> This migration modernizes the Zava Queue Bridge application to the latest .NET
> framework and deploys it to Azure. The application currently processes local
> dropped files and publishes messages through RabbitMQ. The new architecture will:
>
> - Move the runtime to the latest supported .NET LTS for long-term support
> - Replace core messaging and file-processing dependencies with Azure-native
>   services to improve cloud alignment and operational resiliency
> - Deploy the modernized workload to Azure with a cloud-native deployment model
>
> The migration follows a phased approach: baseline upgrade, targeted Azure service
> modernization, security hardening, and production deployment.

---

## Migration Impact Summary

| Application | Original Service | New Azure Service | Authentication | Comments |
|-------------|------------------|-------------------|----------------|----------|
| ZavaQueueBridge | .NET Framework 4.8 runtime | .NET 10 runtime | N/A | Modernize to latest framework |
| ZavaQueueBridge | RabbitMQ broker | Azure Service Bus | Managed Identity | Cloud-native messaging |
| ZavaQueueBridge | Local filedrop path | Azure mounted storage | Managed Identity | Cloud-native file ingestion |
| ZavaQueueBridge | Local/legacy hosting | Azure Container Apps | Managed Identity | Deploy to Azure |
