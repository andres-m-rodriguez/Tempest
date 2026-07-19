using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Tempest;
using Tempest.Demo.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddTempest();
builder.Services.AddSingleton<TodoApi>();

await builder.Build().RunAsync();
