# Modernization Plan: modernization-plan

**Project**: ZavaQueueBridge

---

## Technical Framework

- **Language**: C#
- **Framework**: .NET Framework 4.8
- **Build Tool**: MSBuild
- **Database**: Not specified
- **Key Dependencies**: Confluent.Kafka, RabbitMQ.Client, Npgsql

---

## Overview

> This migration modernizes application configuration and secret handling for cloud readiness. The application currently stores sensitive values and connection strings in local configuration and code. The new architecture will:
>
> - Move sensitive configuration to Azure-native secret/configuration services
> - Reduce credential exposure risk by using identity-based access patterns
> - Improve operational security posture before deployment
>
> The migration follows a phased approach: externalize configuration first, then complete security remediation.

---

## Migration Impact Summary

| Application | Original Service | New Azure Service | Authentication | Comments |
|-------------|------------------|-------------------|----------------|----------|
| ZavaQueueBridge | Local app.config secrets | Azure Key Vault | Managed Identity | Remove hardcoded sensitive values |
| ZavaQueueBridge | Local connection strings | Azure App Configuration | Managed Identity | Replace static config with centralized config |

---

## Security Compliance

Scan and remediate CVEs in project dependencies after modernization changes so the application can be deployed without known vulnerable package versions.
