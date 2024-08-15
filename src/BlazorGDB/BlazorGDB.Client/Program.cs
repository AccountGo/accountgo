using System.Reflection.Metadata;
using BlazorGDB.Client;
using BlazorGDB.Client.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<AuthenticationStateProvider, PersistentAuthenticationStateProvider>();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(Constants.API_URL) });
//builder.Services.AddScoped(sp => new HttpClient());

await builder.Build().RunAsync();
