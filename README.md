# Velox Platform

Velox is a high-performance, cloud-first asset operations and refurbishment platform designed for high-throughput laptop and hardware processing facilities.

## Architecture

Velox comprises four synchronized apps:
- **VeloxBench**: Bench QC diagnostic client for hardware testing and certification.
- **VeloxManager**: Central warehouse cockpit for asset inventory, real-time Kanban tracking, technician workload, and invoicing.
- **VeloxLink**: Lightweight employee companion PWA for mobile job assignment and asset lookup.
- **VeloxAPI**: Authoritative cloud backend powered by ASP.NET Core (.NET 10), SQLite WAL, and Server-Sent Events (SSE).

## Projects

- `Velox.Shared`: Cross-project models, DTOs, and lifecycle enums.
- `VeloxAPI`: Cloud REST API and real-time event streaming service.
- `VeloxAPI.Tests`: Comprehensive integration and regression test suite.

## Development

Build solution:
```powershell
dotnet build Velox.slnx
```

Run test suite:
```powershell
dotnet test Velox.slnx
```

Deploying VeloxAPI:
- Deploys to Railway via `Dockerfile` and `railway.toml`.
- Persistent storage mounted at `/data` for SQLite database.
- Health check available at `/health`.
