namespace Dto.Common
{
    public class Contact : BaseDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public Party Party { get; set; }

        public Contact() {
            Party = new Party();
        }
    }
}
