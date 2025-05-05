# Rally Identity Provider (IdP)

## Overview

Rally IdP is the central OpenID Connect (OIDC) and OAuth 2.0 Identity Provider for the Rally Group of brands. Its primary purpose is to provide a single, secure point of authentication for users across diverse brand applications, enabling Single Sign-On (SSO) and laying the foundation for future cross-brand initiatives like loyalty programs.

This service allows users to register and log in once with their Rally account and then access affiliated brand applications (such as Rally Motors, Hetro Clothing, Dental Clinics, etc.) without needing separate credentials for each.

**Current Status:** In Development. Core OIDC flows are functional for testing, basic UI is implemented, and initial security hardening steps are complete. **This is NOT yet production-ready.**

## Technology Stack

*   **Framework:** .NET 9 (Preview) / ASP.NET Core (Razor Pages)
*   **OIDC/OAuth2 Server:** Duende IdentityServer v7
*   **User Management:** ASP.NET Core Identity
*   **Database:** PostgreSQL
*   **Data Access:** Entity Framework Core (EF Core) with Npgsql provider
*   **Logging:** Serilog (Console, Rolling File, Custom Discord Sink via Map)
*   **Security Headers:** NetEscapades.AspNetCore.SecurityHeaders
*   **Email (Dev):** System.Net.Mail with Gmail App Password (Plan to use AWS SES for Production)

## Features Implemented

*   **Core OIDC Provider:** Serves discovery document (`/.well-known/openid-configuration`) and JWKS URI.
*   **User Authentication:** Local user registration, login, logout using ASP.NET Core Identity.
*   **Password Reset:** Multi-stage flow using a 6-digit code sent via email.
*   **Account Lockout:** Enabled after multiple failed login attempts.
*   **OIDC Flows:** Supports Authorization Code Flow with PKCE.
*   **Consent:** Basic user consent page for requested scopes.
*   **Database Persistence:**
    *   User Accounts (`AspNetUsers`, etc.) via `ApplicationDbContext`.
    *   OIDC Client/Resource Configuration (`Clients`, `IdentityResources`, etc.) via `ConfigurationDbContext`.
    *   OIDC Operational Data (Grants, Tokens) via `PersistedGrantDbContext`.
    *   ASP.NET Core Data Protection Keys via `ApplicationDbContext`.
*   **Configuration:**
    *   Development-time seeding for Clients and Identity Resources (`Configuration/Config.cs`).
    *   Secure loading of signing certificate from PFX file.
    *   Secure configuration via .NET User Secrets for development (DB connection, Cert password, Email password, Discord webhook).
*   **Security:**
    *   Replaced developer signing credential with a persistent self-signed certificate.
    *   Basic security headers applied via middleware (CSP includes `'unsafe-inline'` for `script-src` currently).
    *   Data Protection keys persisted to database.
    *   Database access configured with a least-privilege user (for dev setup).
*   **Logging & Alerting:**
    *   Structured logging to Console and Rolling File using Serilog.
    *   Targeted alerts for critical events (Login Failures, Lockouts, Fatal Errors, Suspicious Behavior) sent to Discord via a custom sink and filtering.
*   **UI:**
    *   Basic dark theme applied to core Identity pages (Login, Register, Consent, Lockout, Forgot/Reset Password flows) using a dedicated layout (`_IdentityLayout.cshtml`).

## Project Structure

*   `/Areas/Identity/Pages/`: Contains the scaffolded ASP.NET Core Identity UI pages (Login, Register, Manage, etc.).
*   `/Configuration/`: Defines static OIDC clients and resources for development seeding (`Config.cs`).
*   `/Data/`: Contains the Entity Framework Core `ApplicationDbContext` and migrations.
*   `/Pages/`: Contains application-specific Razor Pages (e.g., `Index`, `Privacy`, `Error`).
*   `/Pages/Consent/`: Contains the custom OIDC Consent page UI and logic.
*   `/Pages/Shared/`: Contains shared layout files (`_Layout.cshtml`, `_IdentityLayout.cshtml`) and partial views (`_LoginPartial`, `_ScopeListItem`, etc.).
*   `/Services/`: Contains custom services like `EmailSender.cs` and `DiscordWebhookSink.cs`.
*   `/wwwroot/`: Static assets (CSS, JS, images).
    *   `css/identity.css`: Custom styles for the authentication UI.
*   `Program.cs`: Application startup, service configuration, middleware pipeline.

## Getting Started (Development)

### Prerequisites

1.  **.NET 9 SDK:** (Ensure correct preview version is installed)
2.  **PostgreSQL:** A running instance accessible locally (Docker recommended).
3.  **Git:** For cloning the repository.
4.  **(Optional) .NET User Secrets Tool:** Usually included with SDK.
5.  **(Optional) PostgreSQL Admin Tool:** (e.g., DBeaver, pgAdmin) for inspecting the database.

### Setup

1.  **Clone:** `git clone <repository-url>`
2.  **Navigate:** `cd Rally`
3.  **Generate/Place Signing Certificate:**
    *   Generate a dev cert: `dotnet dev-certs https --export-path ./rally-signing-cert.pfx --password YOUR_PFX_PASSWORD`
    *   Move `rally-signing-cert.pfx` to a secure location *outside* the repository (e.g., `/Users/youruser/secure_certs/`).
4.  **Configure User Secrets:** Initialize secrets (`dotnet user-secrets init` if needed) and set the required values:
    ```bash
    dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=rally_db_dev;Username=rally_idp_user;Password=YOUR_DB_PASSWORD;Include Error Detail=true"
    dotnet user-secrets set "IdentityServer:SigningCertPath" "/path/to/your/secure/location/rally-signing-cert.pfx"
    dotnet user-secrets set "IdentityServer:SigningCertPassword" "YOUR_PFX_PASSWORD"
    dotnet user-secrets set "Smtp:Server" "smtp.gmail.com"
    dotnet user-secrets set "Smtp:Port" "587"
    dotnet user-secrets set "Smtp:SenderName" "Rally Auth Dev"
    dotnet user-secrets set "Smtp:SenderEmail" "YOUR_GMAIL_ADDRESS@gmail.com"
    dotnet user-secrets set "Smtp:Username" "YOUR_GMAIL_ADDRESS@gmail.com"
    dotnet user-secrets set "Smtp:Password" "YOUR_GMAIL_APP_PASSWORD" # Use 16-char App Password
    dotnet user-secrets set "Discord:WebhookUrl" "YOUR_DISCORD_WEBHOOK_URL"
    ```
5.  **Create Database User (if not done):** Ensure the `rally_idp_user` exists in PostgreSQL with the correct password and permissions on the `rally_db_dev` database (see previous instructions/scripts).
6.  **Apply Database Migrations:**
    ```bash
    dotnet ef database update -c ApplicationDbContext
    dotnet ef database update -c ConfigurationDbContext
    dotnet ef database update -c PersistedGrantDbContext
    ```
7.  **Run the Application:**
    ```bash
    dotnet run --launch-profile https
    ```
8.  **Access:** Navigate to the HTTPS URL (e.g., `https://localhost:7223`). Check the discovery document at `/.well-known/openid-configuration`.

## Security Considerations (IMPORTANT)

*   **DEVELOPMENT ONLY:** This setup uses a self-signed certificate and development-specific secrets management (User Secrets). **DO NOT DEPLOY TO PRODUCTION AS IS.**
*   **Production Certificates:** A valid, trusted X.509 certificate from a proper CA (or internal CA) MUST be used for production.
*   **Production Secrets:** Connection strings, certificate passwords, email passwords, webhook URLs, and client secrets MUST be stored securely using environment variables, Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, or a similar secure mechanism.
*   **Client Secrets:** The `Config.cs` seeding approach exposes plain-text secrets before hashing. In production, clients should be managed via a secure admin interface or database tooling, storing only hashed secrets.
*   **CSP:** The current Content Security Policy includes `'unsafe-inline'` for `script-src` to support OIDC `form_post`. This should be revisited and potentially replaced with hashes/nonces for better security.
*   **Further Hardening:** Review and implement remaining steps from Phase 4 (HTTPS/HSTS, Forwarded Headers, Identity Options, CORS, Dependency Scanning, Monitoring, Patching, Auditing, Backup/Recovery, MFA, Pen Testing, WAF) before production deployment.

## Current Limitations / TODO

*   Implement Production Certificate strategy.
*   Implement Production Secrets Management strategy.
*   Replace development email sender (Gmail) with production service (e.g., AWS SES).
*   Refine Content Security Policy (`script-src`) to remove `'unsafe-inline'`.
*   Implement robust Unit and Integration tests.
*   Implement comprehensive API Protection features (if required).
*   Build realistic client applications (Hetro, Rally Motors, Dental).
*   Create developer documentation for client integration.
*   Implement MFA.
*   Build an Admin UI/Tool for managing users, clients, and resources securely.
*   Implement remaining Phase 4 hardening steps.