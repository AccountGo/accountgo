using AccountGoWeb.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using AutoMapper;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Mapping
builder.Services.AddAutoMapper(typeof(Program));

builder.Services.AddControllersWithViews();

// string urlhost = System.Environment.GetEnvironmentVariable("APIHOST") ?? "localhost";
// string urlport = System.Environment.GetEnvironmentVariable("APIPORT") ?? "8001";
string apiurl = System.Environment.GetEnvironmentVariable("APIURL") ?? "http://localhost:8001/api/";
builder.Configuration["ApiUrl"] = apiurl;
System.Console.WriteLine($"[ASPNETCORE SERVER] API URL {builder.Configuration["ApiUrl"]}");

builder.Services.AddHttpClient();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => o.LoginPath = new PathString("/account/signin"));

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddCircuitOptions(options => options.DetailedErrors = true); // for debugging razor components

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAntiforgery();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
