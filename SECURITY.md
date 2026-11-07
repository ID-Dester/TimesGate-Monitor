# Security policy

## Reporting a vulnerability

Please do not disclose a vulnerability, LocalToken, personal IP address or diagnostic archive in a public issue.

After the repository is published, use GitHub's **Security → Report a vulnerability** flow. Include the TGm version, Windows version, a minimal reproduction and sanitized logs. Do not attach the contents of Windows Credential Manager.

## Security model

TGm communicates with Times Gate only over the local network. The desktop application runs without elevation. The optional `TGm.SensorHost` scheduled task runs as `SYSTEM`, accepts requests through a named pipe restricted to the Windows SID that enabled it, and exits after its idle timeout.

The LocalToken is stored in Windows Credential Manager for the current user and is intentionally excluded from dashboard JSON and application logs.
