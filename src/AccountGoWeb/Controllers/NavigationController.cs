using AccountGoWeb.Models.SubMenu;
using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class NavigationController : Controller
    {
        public IActionResult Index()
        {
            var model = new NavigationModel
            {
                SubMenus = new List<SubMenuModel>()
                {
                    new SubMenuModel
                    {
                        Icon = "industry",
                        MenuTitle = "Accounts Receivable",
                        MenuItems = new List<MenuItem>
                        {
                            new MenuItem { Controller = "Quotations", Action = "Quotations", Text = "Sales Quotations" },
                            new MenuItem { Controller = "Sales", Action = "SalesOrders", Text = "Sales Orders" },
                            new MenuItem { Controller = "Sales", Action = "SalesReceipts", Text = "Sales Receipts" },
                            new MenuItem { Controller = "Sales", Action = "SalesInvoices", Text = "Sales Invoices" },
                            new MenuItem { Controller = "Sales", Action = "DonationInvoices", Text = "Donation Invoices" },
                            new MenuItem { Controller = "Sales", Action = "Customers", Text = "Customers" }
                        }
                    },
                    new SubMenuModel
                    {
                        Icon = "link",
                        MenuTitle = "Accounts Payable",
                        MenuItems = new List<MenuItem>
                        {
                            new MenuItem { Controller = "Purchasing", Action = "PurchaseOrders", Text = "Purchase Orders" },
                            new MenuItem { Controller = "Purchasing", Action = "PurchaseInvoices", Text = "Purchase Invoices" },
                            new MenuItem { Controller = "Purchasing", Action = "Vendors", Text = "Vendors" }
                        }
                    },
                    new SubMenuModel
                    {
                        Icon = "wrench",
                        MenuTitle = "Inventory Control",
                        MenuItems = new List<MenuItem>
                        {
                            new MenuItem { Controller = "Inventory", Action = "Index", Text = "Items" },
                            new MenuItem { Controller = "Inventory", Action = "ICJ", Text = "Inventory Control Journal" },
                        }
                    },
                    new SubMenuModel
                    {
                        Icon = "bank",
                        MenuTitle = "Financials",
                        MenuItems = new List<MenuItem>
                        {
                            new MenuItem { Controller = "Financials", Action = "JournalEntries", Text = "Journal Entries" },
                            new MenuItem { Controller = "Financials", Action = "GeneralLedger", Text = "General Ledgers" },
                            new MenuItem { Controller = "Tax", Action = "Taxes", Text = "Taxes" },
                            new MenuItem { Controller = "Financials", Action = "Accounts", Text = "Chart of Accounts" }, 
                            new MenuItem { Controller = "Financials", Action = "Banks", Text = "Banks" }
                        }
                    },
                    new SubMenuModel
                    {
                        Icon = "chart",
                        MenuTitle = "Reports",
                        MenuItems = new List<MenuItem>
                        {
                            new MenuItem { Controller = "Financials", Action = "BalanceSheet", Text = "Balance Sheet" },
                            new MenuItem { Controller = "Financials", Action = "IncomeStatement", Text = "Income Statement" },
                            new MenuItem { Controller = "Financials", Action = "TrialBalance", Text = "Trial Balance" },
                        }
                    },
                    new SubMenuModel
                    {
                        Icon = "group",
                        MenuTitle = "Organization Settings",
                        MenuItems = new List<MenuItem>
                        {
                            new MenuItem { Controller = "Administration", Action = "Company", Text = "Company" },
                            new MenuItem { Controller = "Administration", Action = "Settings", Text = "Settings" },
                        }
                    }
                }
            };
            return View(model);
        }
    }
}
