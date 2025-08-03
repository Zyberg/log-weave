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

var aa = a.DoSomething(12, 15);
var b = aa * 19;
