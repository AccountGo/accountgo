namespace AccountGoWeb.Models.SubMenu
{
    public class SubMenuModel
    {
        public required string Icon { get; set; }
        public required string MenuTitle { get; set; }
        public required List<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
    }

    public class MenuItem
    {
        public required string Controller { get; set; }
        public required string Action { get; set; }
        public required string Text { get; set; }
    }

}