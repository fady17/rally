# Dockerfile - For Staging/Production Builds

# ---- Build Stage ----
    FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
    WORKDIR /source
    
    # Copy solution and project files first for layer caching
    COPY *.sln .
    COPY Rally/*.csproj ./Rally/
    # Add other project folders if you have class libraries etc.
    # COPY YourLibrary/YourLibrary.csproj ./YourLibrary/
    
    # Restore dependencies
    RUN dotnet restore Rally/Rally.csproj
    
    # Copy the rest of the source code
    COPY . .
    
    # Build and publish the application for release
    WORKDIR /source/Rally
    RUN dotnet publish -c Release -o /app/publish --no-restore
    
    # ---- Runtime Stage ----
    FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
    WORKDIR /app
    
    # Optional: Create a non-root user for security
    # RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
    # USER appuser
    
    # Copy published output from the build stage
    COPY --from=build /app/publish .
    
    # Copy the PFX certificate into the image (ALTERNATIVE to volume mount)
    # If using this, ensure PFX is copied to context and adjust path in compose env var
    # COPY rally-signing-cert.pfx /etc/ssl/certs/
    
    # Expose the port the app listens on (HTTP, as TLS is handled externally)
    EXPOSE 8080
    
    # Entry point to run the application DLL
    ENTRYPOINT ["dotnet", "Rally.dll"]