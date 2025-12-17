# Dockerfile for FlockCopilot API (AMD64/x64)
# Targets Azure Container Apps (linux/amd64)
# Build with: docker build or docker buildx build --platform linux/amd64

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY FlockCopilot.Api.csproj .
RUN dotnet restore -a x64

# Copy all source files
COPY . .

# Build and publish for x64
RUN dotnet publish -a x64 -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Expose HTTP port (Container Apps standard)
EXPOSE 8080

# Configure ASP.NET Core to listen on port 8080
ENV ASPNETCORE_URLS=http://+:8080

# Health check endpoint
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "FlockCopilot.Api.dll"]
