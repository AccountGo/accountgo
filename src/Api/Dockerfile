# Multi-stage build for AccountGo API (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Copy Directory props/targets
COPY Directory.Build.props Directory.Build.targets ./

# Copy project files for caching
COPY src/Api/Api.csproj src/Api/
COPY src/Core/Core.csproj src/Core/
COPY src/Dto/Dto.csproj src/Dto/
COPY src/Services/Services.csproj src/Services/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/src.ServiceDefaults/src.ServiceDefaults.csproj src/src.ServiceDefaults/

# Restore dependencies
RUN dotnet restore src/Api/Api.csproj

# Copy source code
COPY src/Api/ src/Api/
COPY src/Core/ src/Core/
COPY src/Dto/ src/Dto/
COPY src/Services/ src/Services/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/src.ServiceDefaults/ src/src.ServiceDefaults/

# Publish release artifacts, compiled ahead of time (ReadyToRun) for the target platform: the container starts on a
# fraction of a vCPU, where JIT-compiling the app at start-up takes minutes.
ARG TARGETARCH
RUN case "$TARGETARCH" in arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac \
    && dotnet publish src/Api/Api.csproj -c Release -r $RID --self-contained false -p:PublishReadyToRun=true -o /app/out

# Determine BUILD_SHA (fallback to 'dev' if missing)
RUN if [ -f src/Api/BUILD_SHA ]; then cp src/Api/BUILD_SHA /app/BUILD_SHA; else echo "dev" > /app/BUILD_SHA; fi

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=build-env /app/out ./
COPY --from=build-env /app/BUILD_SHA /app/BUILD_SHA

ENV ASPNETCORE_URLS=http://+:8001
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DatabaseProvider=Sqlite
# Small-container settings: workstation GC keeps memory well inside 256 MiB, and without dynamic PGO the runtime
# doesn't spend CPU instrumenting code while the app starts.
ENV DOTNET_gcServer=0
ENV DOTNET_TieredPGO=0

EXPOSE 8001

ENTRYPOINT ["dotnet", "Api.dll"]
