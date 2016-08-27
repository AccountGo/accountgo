namespace Dto.Security
{
    public class Permission : BaseDto
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public Group Group { get; set; }
    }
}
