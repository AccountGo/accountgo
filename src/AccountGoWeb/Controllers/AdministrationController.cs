using Dto.Administration;
using Dto.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
  [Microsoft.AspNetCore.Authorization.Authorize]
  public class AdministrationController : BaseController
  {
    public AdministrationController(IConfiguration config)
    {
      _baseConfig = config;
      Models.SelectListItemHelper._config = config;
    }

    public IActionResult Company()
    {
      ViewBag.PageContentHeader = "Company";
      var model = GetAsync<Company>("administration/company").Result;
      if (model == null)
        model = new Company();
      return View(model);
    }

    [HttpPost]
    public IActionResult Company(Company model)
    {
      ViewBag.PageContentHeader = "Company";
      if (ModelState.IsValid)
      {
        var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
        var content = new StringContent(serialize);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = PostAsync("administration/savecompany", content);

        return View(model);
      }
      return View(model);
    }

    public IActionResult Settings()
    {
      ViewBag.PageContentHeader = "Setup and Configuration";
      ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
      var model = GetAsync<GeneralLedgerSetting>("administration/settings").Result;
      if (model == null)
        model = new GeneralLedgerSetting();
      return View(model);
    }

    [HttpPost]
    public IActionResult SaveSettings(Models.Financial.GeneralLedgerSetting model)
    {
      if (ModelState.IsValid)
      {

      }
      ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
      ViewBag.PageContentHeader = "Setup and Configuration";
      return RedirectToAction(nameof(AdministrationController.Settings));
    }

    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "SystemAdministrators")]
    public async System.Threading.Tasks.Task<IActionResult> Users()
    {
      var users = await GetAsync<System.Collections.Generic.IEnumerable<User>>("administration/users");
      ViewBag.PageContentHeader = "Users";
      return View(users);
    }

    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "SystemAdministrators")]
    public async System.Threading.Tasks.Task<IActionResult> Roles()
    {
      var roles = await GetAsync<System.Collections.Generic.IEnumerable<Role>>("administration/roles");
      ViewBag.PageContentHeader = "Security Roles";
      return View(roles);
    }

    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "SystemAdministrators")]
    public async System.Threading.Tasks.Task<IActionResult> Groups()
    {
      var groups = await GetAsync<System.Collections.Generic.IEnumerable<Group>>("administration/groups");
      ViewBag.PageContentHeader = "Security Groups";
      return View(groups);
    }

    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "SystemAdministrators")]
    public async System.Threading.Tasks.Task<IActionResult> AuditLogs()
    {
      var auditLogs = await GetAsync<System.Collections.Generic.IEnumerable<AuditLog>>("administration/auditlogs");

      ViewBag.PageContentHeader = "Audit Logs";
      return View(model: auditLogs);
    }

    [HttpGet]
    public new IActionResult User(int id = 0)
    {
      if (id != 0)
      {
        ViewBag.PageContentHeader = "User";
      }
      else
      {
        ViewBag.PageContentHeader = "New User";
      }

      return View(new Models.Account.RegisterViewModel());
    }

    [HttpPost]
    public new IActionResult User(Models.Account.RegisterViewModel model)
    {
      try
      {
        if (ModelState.IsValid)
        {
          var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
          var content = new StringContent(serialize);
          content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
          HttpResponseMessage responseAddNewUser = Post("account/addnewuser", content);
          Newtonsoft.Json.Linq.JObject resultAddNewUser = Newtonsoft.Json.Linq.JObject.Parse(responseAddNewUser.Content.ReadAsStringAsync().Result);

          if ((bool)resultAddNewUser["succeeded"])
          {
            return RedirectToAction(nameof(AdministrationController.Users), "Administration");
          }
          else
          {
            ModelState.AddModelError(string.Empty, resultAddNewUser["errors"][0]["description"].ToString());
            return View(model);
          }
        }
      }
      catch (Exception ex)
      {
        ModelState.AddModelError(string.Empty, "Please check if your database is ready/published." + ": " + ex.Message);
        return View(model);
      }
      ViewBag.PageContentHeader = "New User";
      return View(model);
    }
  }
}
