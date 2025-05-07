# Rally Identity Provider (IdP)

## Overview

Rally IdP is the central OpenID Connect (OIDC) and OAuth 2.0 Identity Provider for the Rally Group of brands. Its primary purpose is to provide a single, secure point of authentication for users across diverse brand applications, enabling Single Sign-On (SSO) and laying the foundation for future cross-brand initiatives like loyalty programs.

This service allows users to initiate login/registration using their email address, receive a one-time code, and securely access affiliated brand applications (such as Rally Motors, Hetro Clothing, Dental Clinics, etc.).

**Current Status:** In Development. Passwordless OIDC flows are functional for testing, UI for core flows (Login/Register via code, Consent, Manage Account basics, Password Reset via code) is implemented with a dark theme, and initial security hardening steps are complete. **This is NOT yet production-ready.**

## Technology Stack

*   **Framework:** .NET 9 (Preview) / ASP.NET Core (Razor Pages)
*   **OIDC/OAuth2 Server:** Duende IdentityServer v7
*   **User Management:** ASP.NET Core Identity (configured for passwordless email code flow)
*   **Database:** PostgreSQL
*   **Data Access:** Entity Framework Core (EF Core) with Npgsql provider
*   **Logging:** Serilog (Console, Rolling File, Custom Discord Sink via Map for alerts)
*   **Security Headers:** NetEscapades.AspNetCore.SecurityHeaders
*   **Email (Dev):** System.Net.Mail with Gmail App Password (Plan to use AWS SES for Production)

## Features Implemented

*   **Core OIDC Provider:** Serves discovery document (`/.well-known/openid-configuration`) and JWKS URI.
*   **Passwordless User Authentication:**
    *   Users initiate login/registration with email (`/Identity/Account/Login`).
    *   A one-time code is sent to their email.
    *   Users verify the code on `/Identity/Account/VerifyLoginCode` to complete login or register a new account (email is auto-confirmed).
*   **Account Management:**
    *   Password Reset: Multi-stage flow using a 6-digit code sent via email (for users who may have set a password previously or if password functionality is re-enabled).
    *   Account Lockout: Enabled after multiple failed code verification or login attempts.
    *   Basic Manage Account pages (Profile, Email, Change/Set Password, 2FA, Personal Data) styled with dark theme.
*   **OIDC Flows:** Supports Authorization Code Flow with PKCE for client applications.
*   **Consent:** Custom-styled user consent page for requested scopes.
*   **Database Persistence:**
    *   User Accounts (`AspNetUsers`, etc.) via `ApplicationDbContext`.
    *   OIDC Client/Resource Configuration (`Clients`, `IdentityResources`, etc.) via `ConfigurationDbContext`.
    *   OIDC Operational Data (Grants, Tokens) via `PersistedGrantDbContext`.
    *   ASP.NET Core Data Protection Keys via `ApplicationDbContext`.
*   **Configuration:**
    *   Development-time seeding for Clients and Identity Resources (`Configuration/Config.cs`).
    *   Secure loading of signing certificate from PFX file.
    *   Secure configuration via .NET User Secrets for development.
*   **Security:**
    *   Replaced developer signing credential with a persistent self-signed certificate.
    *   Basic security headers applied (CSP includes `'unsafe-inline'` for `script-src` & `style-src` currently).
    *   Data Protection keys persisted to database.
    *   Database access configured with a least-privilege user (for dev setup).
*   **Logging & Alerting:**
    *   Structured logging to Console and Rolling File using Serilog.
    *   Targeted alerts for critical events (Login Failures, Lockouts, Fatal Errors, Suspicious Behavior) sent to Discord.
*   **UI:**
    *   Consistent dark theme applied to core Identity pages (Login/Register via code, Consent, Lockout, Forgot/Reset Password flows, Manage Account) using a dedicated layout (`_IdentityLayout.cshtml`).
    *   Main IdP Index page uses standard layout for dev testing links.

## Project Structure (Key Areas)

*   `/Areas/Identity/Pages/Account/`: Passwordless login/registration (`Login.cshtml`, `VerifyLoginCode.cshtml`), Logout, Manage Account pages.
*   `/Configuration/`: Defines static OIDC clients/resources for dev seeding (`Config.cs`).
*   `/Pages/Consent/`: Custom OIDC Consent page.
*   `/Pages/Shared/_IdentityLayout.cshtml`: Dedicated layout for authentication flows.
*   `/Services/`: Custom services (`EmailSender.cs`, `DiscordWebhookSink.cs`).
*   `Program.cs`: Application startup, service configuration (including Serilog, IdentityServer, Identity options).

## Getting Started (Development)

### Prerequisites

1.  **.NET 9 SDK**
2.  **PostgreSQL** (local instance or Docker)
3.  **Git**
4.  **(Optional) PostgreSQL Admin Tool**

### Setup

1.  **Clone:** `git clone <repository-url>`
2.  **Navigate:** `cd Rally`
3.  **Generate/Place Signing Certificate:**
    *   `dotnet dev-certs https --export-path ./rally-signing-cert.pfx --password YOUR_PFX_PASSWORD`
    *   Move `rally-signing-cert.pfx` to a secure location (e.g., `/Users/youruser/secure_certs/`).
4.  **Configure User Secrets:** (`dotnet user-secrets init` if needed)
    ```bash
    dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=rally_db_dev;Username=rally_idp_user;Password=YOUR_DB_PASSWORD;Include Error Detail=true"
    dotnet user-secrets set "IdentityServer:SigningCertPath" "/path/to/your/rally-signing-cert.pfx"
    dotnet user-secrets set "IdentityServer:SigningCertPassword" "YOUR_PFX_PASSWORD"
    dotnet user-secrets set "Smtp:Server" "smtp.gmail.com"
    dotnet user-secrets set "Smtp:Port" "587"
    dotnet user-secrets set "Smtp:SenderName" "Rally Auth Dev"
    dotnet user-secrets set "Smtp:SenderEmail" "YOUR_GMAIL_ADDRESS@gmail.com"
    dotnet user-secrets set "Smtp:Username" "YOUR_GMAIL_ADDRESS@gmail.com"
    dotnet user-secrets set "Smtp:Password" "YOUR_GMAIL_APP_PASSWORD"
    dotnet user-secrets set "Discord:WebhookUrl" "YOUR_DISCORD_WEBHOOK_URL"
    ```
5.  **Database User:** Ensure `rally_idp_user` exists in PostgreSQL with necessary permissions for `rally_db_dev`.
6.  **Apply Migrations:**
    ```bash
    dotnet ef database update -c ApplicationDbContext
    dotnet ef database update -c ConfigurationDbContext
    dotnet ef database update -c PersistedGrantDbContext
    ```
7.  **Run:** `dotnet run --launch-profile https`
8.  **Access:** IdP at `https://localhost:7223` (or your configured port). Test client (e.g., Hetro) at its respective port.

## Security Considerations (IMPORTANT - Review Before Production)

*   **Production Certificates & Secrets:** Replace dev certs and User Secrets with production-grade solutions (CA certs, Env Vars/Vault).
*   **Client Secret Management:** For production, Client Secrets for IdP clients (defined in `Config.cs`) should be managed via a secure admin interface or database tooling, not hardcoded then hashed.
*   **CSP:** Refine `script-src` and `style-src` to remove `'unsafe-inline'` by using hashes or nonces if possible.
*   **Email Service:** Replace Gmail SMTP with a robust service like AWS SES for production.
*   **Complete Hardening:** Implement remaining Phase 4 steps (HTTPS/HSTS on proxy, Forwarded Headers, full review of Identity Options, CORS if needed, Dependency Scanning, Monitoring, Patching, Auditing, Backup/Recovery, MFA, Pen Testing, WAF).

## Current Limitations / TODO

*   Implement production certificate, secrets, and email strategies.
*   Refine Content Security Policy.
*   Implement robust Unit and Integration tests.
*   Build realistic client applications for all brands.
*   Create comprehensive developer documentation for client integration (see separate guide).
*   (Future) Explore advanced features: MFA, API Protection, Admin UI for IdP management.