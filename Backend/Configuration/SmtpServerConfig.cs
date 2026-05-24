namespace SmtpRelay.App.Configuration;

public class SmtpServerConfig
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 25;
    public string HostName { get; set; } = "localhost";
    public int MaxMessageSizeMB { get; set; } = 25;
    public bool RequireAuthentication { get; set; } = false;
    public string AllowedRecipientDomains { get; set; } = "*";
    public SmtpServerAuthConfig Auth { get; set; } = new();
}

public class SmtpServerAuthConfig
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}