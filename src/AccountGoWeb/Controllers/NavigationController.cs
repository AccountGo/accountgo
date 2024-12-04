using Microsoft.AspNetCore.Mvc;
using AccountGoWeb.Models.Navigation;
public class NavigationViewComponent : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var menuItems = new List<NavigationItem>
        {
            new NavigationItem { Controller = "Quotations", Action = "Quotations", Text = "Sales Quotations" },
            new NavigationItem { Controller = "Sales", Action = "SalesOrders", Text = "Sales Orders" },
            new NavigationItem { Controller = "Sales", Action = "SalesReceipts", Text = "Sales Receipts" },
            new NavigationItem { Controller = "Sales", Action = "SalesInvoices", Text = "Sales Invoices" },
            new NavigationItem { Controller = "Sales", Action = "DonationInvoices", Text = "Donation Invoices" },
            new NavigationItem { Controller = "Sales", Action = "Customers", Text = "Customers" }
        };

        // Check if menuItems is null or empty and handle accordingly
        if (menuItems == null || !menuItems.Any())
        {
            // Optionally, you can log this or return a fallback view
            // You could return a default view or an empty view, like:
            return View("EmptyMenu"); // Create this view if desired
        }

        return View("AccountReceivableMenu", menuItems); 
    }
}
