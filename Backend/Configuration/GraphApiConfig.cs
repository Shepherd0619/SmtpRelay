namespace SmtpRelay.App.Configuration;

public class GraphApiConfig
{
    public bool Enabled { get; set; } = false;
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string UserId { get; set; } = "";
    public bool SaveToSentItems { get; set; } = true;
    public bool CreateDraftsBeforeSending { get; set; } = true;
}