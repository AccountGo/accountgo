namespace AccountGoWeb.Models.SubMenu
{
    public class NavigationModel
    {
        public List<SubMenuModel>? SubMenus { get; set; }
    }

    public class SubMenuModel
    {
        public string? Icon { get; set; }
        public string? MenuTitle { get; set; }
        public List<MenuItem>? MenuItems { get; set; }
    }

    public class MenuItem
    {
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public string? Text { get; set; }
    }
}