namespace SmtpRelay.App.Configuration;

public class QueueConfig
{
    public string DatabasePath { get; set; } = "Data/queue.db";
    public int MaxRetryCount { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 60;
    public int ProcessingIntervalSeconds { get; set; } = 5;
    public int MaxConcurrentProcessing { get; set; } = 1;
    public int RetentionDays { get; set; } = 7;
}