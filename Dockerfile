# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["Backend/BankingApi/BankingApi.csproj", "Backend/BankingApi/"]
COPY ["Backend/BankingApi.Tests/BankingApi.Tests.csproj", "Backend/BankingApi.Tests/"]
COPY ["Backend/BankingApi.sln", "Backend/"]
RUN dotnet restore "Backend/BankingApi.sln"

# Copy full source and build
COPY Backend/ Backend/
WORKDIR /src/Backend/BankingApi
RUN dotnet build "BankingApi.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "BankingApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Install curl for healthcheck
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

USER app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BankingApi.dll"]
