namespace Web.Models
{
    public abstract partial class ModelBase
    {        
    }

    public abstract partial class EntityModelBase : ModelBase
    {
        public virtual int Id { get; set; }
    }
}