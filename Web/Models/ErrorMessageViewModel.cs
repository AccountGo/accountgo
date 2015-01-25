namespace Web.Models
{
    public class ErrorMessageViewModel
    {
        private readonly string message;

        public ErrorMessageViewModel(string message)
        {
            this.message = message;
        }

        public string Message
        {
            get { return message; }
        }
    }
}