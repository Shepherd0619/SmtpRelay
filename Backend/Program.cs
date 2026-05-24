using SmtpRelay.App.Extensions;
using SmtpRelay.App.Services.Queue;

namespace SmtpRelay.App;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSmtpRelay(builder.Configuration);

        var app = builder.Build();

        // Configure the HTTP request pipeline
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();

        // Initialize the queue database before accepting traffic
        var queueService = app.Services.GetRequiredService<IQueueService>();
        await queueService.InitializeAsync();

        await app.RunAsync();
    }
}