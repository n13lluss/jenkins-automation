# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PipelineForge.slnx ./
COPY src/PipelineForge.Core/PipelineForge.Core.csproj src/PipelineForge.Core/
COPY src/PipelineForge.Infrastructure/PipelineForge.Infrastructure.csproj src/PipelineForge.Infrastructure/
COPY src/PipelineForge.Api/PipelineForge.Api.csproj src/PipelineForge.Api/
RUN dotnet restore src/PipelineForge.Api/PipelineForge.Api.csproj
COPY src/ src/
RUN dotnet publish src/PipelineForge.Api/PipelineForge.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Chiseled image: no shell, no package manager, runs as non-root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
COPY --from=build /app ./
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PipelineForge.Api.dll"]
