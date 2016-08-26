namespace Dto
{
    public abstract class BaseDto
    {
        public virtual int Id { get; set; }
        public virtual string ModifiedBy { get { return Singleton.Instance.UserName; } }        
    }
}
