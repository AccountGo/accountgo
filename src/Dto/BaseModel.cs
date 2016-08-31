namespace Dto
{
    public abstract class BaseDto
    {
        public virtual int Id { get; set; }
        public string ModifiedBy { get; set; }        
    }
}
