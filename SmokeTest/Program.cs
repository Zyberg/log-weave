using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmokeTest;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddSingleton<Test>();

builder.Services.AddLogging(config =>
    {
        config.AddConsole();              // 👈 Required
        config.SetMinimumLevel(LogLevel.Information);
    });

var app = builder.Build();

var a = app.Services.GetRequiredService<Test>();

a.DoSomething(12);
