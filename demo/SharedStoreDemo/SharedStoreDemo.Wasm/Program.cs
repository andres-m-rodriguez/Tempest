using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SharedStoreDemo.Shared;
using SharedStoreDemo.Wasm;
using Tempest;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddTempest();
builder.Services.AddScoped<DemoStore>(sp => new DemoStore(sp.GetRequiredService<IEventBus>()));

await builder.Build().RunAsync();
