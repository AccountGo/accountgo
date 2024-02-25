using Blazored.LocalStorage;
using Blazored.Toast;
using Dto.Contracts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebBlazor;
using WebBlazor.Authentication;
using WebBlazor.Rx;
using WebBlazor.Services;
using WebBlazor.Services.Contracts;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:8001") });
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();
builder.Services.AddScoped<IUserAccount, UserAccountService>();
builder.Services.AddScoped<ITaxService, TaxService>();
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddSingleton<LoaderRx>();
builder.Services.AddSingleton<LookupRx>();
await builder.Build().RunAsync();
