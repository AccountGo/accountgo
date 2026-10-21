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

# Publish release artifacts
RUN dotnet publish src/Api/Api.csproj -c Release -o /app/out

# Determine BUILD_SHA (fallback to 'dev' if missing)
RUN if [ -f src/Api/BUILD_SHA ]; then cp src/Api/BUILD_SHA /app/BUILD_SHA; else echo "dev" > /app/BUILD_SHA; fi

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=build-env /app/out ./
COPY --from=build-env /app/BUILD_SHA /app/BUILD_SHA

# Pre-seeded SQLite database
COPY src/Api/accountgo.db* ./

ENV ASPNETCORE_URLS=http://+:8001
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DatabaseProvider=Sqlite

EXPOSE 8001

ENTRYPOINT ["dotnet", "Api.dll"]
