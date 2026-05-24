namespace SmtpRelay.App.Configuration;

public class SmtpOutputConfig
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
}