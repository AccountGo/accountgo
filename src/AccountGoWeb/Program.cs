using AccountGoWeb.Components;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Mapping
builder.Services.AddAutoMapper(typeof(Program));

builder.Services.AddControllersWithViews();

string apiurl = System.Environment.GetEnvironmentVariable("APIURL") ?? "http://localhost:8001/api/";

builder.Configuration["ApiUrl"] = apiurl;
Console.WriteLine($"[ASPNETCORE SERVER] API URL {builder.Configuration["ApiUrl"]}");

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
    // In production, use the Error endpoint for handling exceptions
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    // In development, show detailed exception pages
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
// Configure custom status code pages (like 404) to use the Error controller
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseAuthentication();
app.UseAntiforgery();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
